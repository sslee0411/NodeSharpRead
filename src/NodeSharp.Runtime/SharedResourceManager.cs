using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 공유 리소스 관리자
/// 역활 및 기능 : 참조 카운트 기반으로 여러 노드가 공유하는 리소스의 시작/종료를 관리
///
/// 여러 노드가 하나의 실제 리소스(TCP 서버, DB 커넥션 등)를 공유할 때, 참조 카운트로 그 리소스의
/// 시작/종료를 한 번만 일어나게 관리합니다(RT-10, 02번 문서 2번 탭 카드2). 같은 <c>id</c>로
/// <see cref="AcquireAsync{T}"/>를 여러 번 불러도(예: 같은 포트를 쓰는 TCP-In 노드가 캔버스에 3개 있어도)
/// 실제 <see cref="ISharedServiceNode.StartAsync"/>는 최초 1회만 호출되고, 참조가 모두 해제(<see cref="ReleaseAsync"/>)
/// 될 때만 <see cref="ISharedServiceNode.StopAsync"/>가 호출됩니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>카드2 원본의 레이스 컨디션 수정</b> — 원본 의사코드는 "①lock 안에서 기존 항목 확인 → 없으면
/// lock 밖에서 <c>factory()</c>+<see cref="ISharedServiceNode.StartAsync"/> 실행 → ②다시 lock을 잡고
/// 등록"하는 2단계 구조입니다. 이 사이(①과 ② 사이, lock이 풀려있는 구간)에 같은 <c>id</c>로
/// <see cref="AcquireAsync{T}"/>가 동시에 두 번 들어오면 둘 다 "기존 항목 없음"으로 판단해 각자
/// <c>factory()</c>+<see cref="ISharedServiceNode.StartAsync"/>를 실행하고, 나중에 등록하는 쪽이 먼저
/// 등록된 항목을 덮어써 버립니다 — 먼저 만들어진 리소스는 <see cref="ISharedServiceNode.StopAsync"/>가
/// 한 번도 불리지 않은 채 참조를 잃어버리는 누수가 됩니다(실제 소켓이 열린 채 아무도 닫지 않게 됨).
/// 이 문제를 피하려고, <see cref="AcquireAsync{T}"/>/<see cref="ReleaseAsync"/> 전체를
/// <see cref="SemaphoreSlim"/>(용량 1) 하나로 감싸 항상 순서대로만 실행되게 했습니다 — 이 매니저를
/// 통한 참조 증감은 노드 배포/종료 시점(드묾)에만 일어나고 메시지 처리 같은 빈번한 경로(초당 여러 번
/// 호출되는 <see cref="FlowEngine.RouteAsync"/> 등)가 아니므로, <see cref="NodeExecutionGate"/>(RT-06)처럼
/// <c>id</c>별로 나눠 잠그는 대신 하나의 잠금으로 단순하게 정확성을 우선했습니다.</item>
/// <item><b><c>Shared</c> 프로퍼티로 <c>NodeContext</c>에 연결하는 작업은 이 Step 범위 밖</b> — 03번
/// Step맵 <c>RT-10</c> 완료 기준은 이 클래스 자체의 동작(참조 카운트 증감, 마지막 참조 해제 때만 실제
/// 종료)만 요구합니다. <c>NodeContext</c>(<c>RT-09b</c>)에 <c>Shared</c> 프로퍼티를 추가해 실제 노드가
/// <c>ctx.Shared.AcquireAsync(...)</c>로 쓰게 하는 배선은, <c>RT-09a</c>→<c>RT-09b</c>→<c>RT-09c</c>에서
/// 확립한 "먼저 재료가 되는 클래스를 독립적으로 완성하고, NodeContext 연동은 필요해지는 시점(예:
/// 실제 <c>TcpServerNode</c>/<c>TcpInNode</c>류가 구현되는 Step)에 별도로"라는 원칙과 동일하게 향후로
/// 미룹니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var manager = new SharedResourceManager();
///
/// // 같은 id로 3번 참조해도 factory/StartAsync는 최초 1번만 실행됨
/// var s1 = await manager.AcquireAsync("srv-5000", () => new MyTcpServer("srv-5000"), CancellationToken.None);
/// var s2 = await manager.AcquireAsync("srv-5000", () => new MyTcpServer("srv-5000"), CancellationToken.None);
/// var s3 = await manager.AcquireAsync("srv-5000", () => new MyTcpServer("srv-5000"), CancellationToken.None);
/// // s1, s2, s3는 모두 같은 인스턴스
///
/// await manager.ReleaseAsync("srv-5000");   // 참조 3 → 2, 아직 StopAsync 안 불림
/// await manager.ReleaseAsync("srv-5000");   // 참조 2 → 1, 아직 StopAsync 안 불림
/// await manager.ReleaseAsync("srv-5000");   // 참조 1 → 0, 이번에만 StopAsync 호출됨
/// </code>
/// </example>
public sealed class SharedResourceManager
{
    private readonly Dictionary<string, (ISharedServiceNode Node, int RefCount)> _services = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// <paramref name="id"/>로 등록된 리소스가 이미 있으면 참조 카운트만 늘리고 그 인스턴스를 반환합니다.
    /// 없으면 <paramref name="factory"/>로 새로 만들고 <see cref="ISharedServiceNode.StartAsync"/>를 호출한
    /// 뒤 참조 카운트 1로 등록합니다.
    /// </summary>
    public async Task<T> AcquireAsync<T>(string id, Func<T> factory, CancellationToken ct) where T : ISharedServiceNode
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_services.TryGetValue(id, out var existing))
            {
                _services[id] = (existing.Node, existing.RefCount + 1);
                return (T)existing.Node;
            }

            var node = factory();
            await node.StartAsync(ct);   // 최초 1회만 실제 리소스 오픈(예: TcpListener.Start)
            _services[id] = (node, 1);
            return node;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// <paramref name="id"/>의 참조 카운트를 1 줄입니다. 0 이하가 되면 등록에서 제거하고
    /// <see cref="ISharedServiceNode.StopAsync"/>를 호출합니다. 등록되지 않은 <paramref name="id"/>는
    /// 조용히 무시합니다(이미 전부 해제됐거나 애초에 <see cref="AcquireAsync{T}"/>를 부른 적이 없는 경우).
    /// </summary>
    public async Task ReleaseAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_services.TryGetValue(id, out var existing)) return;

            var count = existing.RefCount - 1;
            if (count <= 0)
            {
                // AcquireAsync의 StartAsync와 동일한 원칙(위 remarks 참고) — 단순함을 위해 잠금 하나로
                // 순서를 보장하므로, StopAsync도 같은 잠금 안에서 호출해 "제거 판정"과 "실제 종료"
                // 사이에 다른 Acquire/Release가 끼어들 여지를 남기지 않는다.
                _services.Remove(id);
                await existing.Node.StopAsync();
            }
            else
            {
                _services[id] = (existing.Node, count);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
