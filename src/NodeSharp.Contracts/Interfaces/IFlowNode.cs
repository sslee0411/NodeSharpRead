using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// 캔버스에 배치되는 모든 노드가 구현해야 하는 최소 계약입니다. Node-RED의 노드 런타임 API
/// (<c>node.on('input', ...)</c> 등)에 대응하며, <c>FlowEngine</c>이 배포·메시지 전달·종료
/// 전 과정에서 이 인터페이스만으로 노드를 다룹니다.
/// 설계 근거: 02번 문서 2번 탭 카드 1.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>ctx 파라미터</b>: <see cref="INodeContext"/>를 받습니다. 실제 구현 클래스인 <c>NodeContext</c>는
/// NodeSharp.Runtime 프로젝트에 있는데, 이 인터페이스는 Contracts 프로젝트에 있습니다. 만약 이 인터페이스가
/// <c>NodeContext</c>를 직접 참조하면 두 프로젝트가 서로를 참조하게 되어(순환 참조) 빌드가 되지 않습니다
/// (v1.57, 02번 문서 2번 탭 카드 1 참고).</item>
/// <item><b>생성</b>: 이 인터페이스는 인스턴스 생성 방법을 정의하지 않습니다 — 노드 타입 메타데이터와
/// 팩토리는 <c>RG-01</c>의 <c>INodeTypeDescriptor</c>가 별도로 담당합니다.</item>
/// <item><b>동시성</b>: 노드별 동시 처리 개수 제한은 <see cref="MaxConcurrency"/> 기본 구현 멤버로
/// 노출합니다(<c>RT-06</c>). 실제 게이트(<c>NodeExecutionGate</c>, <c>NodeSharp.Runtime</c>)는 배포된
/// <see cref="NodeConfig.MaxConcurrency"/>(사용자가 Editor에서 설정한 값)를 우선 사용하고, 배포 정보를
/// 찾을 수 없을 때만 이 기본 구현 멤버로 대체합니다 — 자세한 내용은 <c>NodeSharp.Runtime.FlowEngine</c>
/// XML 주석(★ RT-06)을 참고하십시오.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 가장 단순한 구현 — 입력을 그대로 다음 노드로 전달하는 패스스루 노드
/// public sealed class PassThroughNode : IFlowNode
/// {
///     public string Id { get; init; } = default!;
///     public string Type => "pass-through";
///     public string Name { get; set; } = "";
///     public IReadOnlyList&lt;NodePort&gt; InputPorts { get; } = new[] { new NodePort(0, "in") };
///     public IReadOnlyList&lt;NodePort&gt; OutputPorts { get; } = new[] { new NodePort(0, "out") };
///
///     public Task OnStartAsync(INodeContext ctx, CancellationToken ct) =&gt; Task.CompletedTask;
///
///     public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =&gt;
///         ctx.RouteAsync(Id, 0, msg, ct);   // 0번 출력 포트로 그대로 전달
///
///     public Task OnCloseAsync(INodeContext ctx) =&gt; Task.CompletedTask;
/// }
///
/// // 2) OnCloseAsync에서 이벤트 구독 해제 — 누락 시 재배포마다 핸들러가 누적되는 누수 발생(공통 규칙 ②)
/// public sealed class AlarmWatchNode : IFlowNode
/// {
///     private IDisposable? _subscription;
///     public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
///     {
///         // _subscription = ctx.EventBus.Subscribe&lt;AlarmRaisedEvent&gt;(e =&gt; ...); // RT-07 이후 사용 가능
///         return Task.CompletedTask;
///     }
///     public Task OnCloseAsync(INodeContext ctx) { _subscription?.Dispose(); return Task.CompletedTask; }
///     // Id/Type/Name/InputPorts/OutputPorts/OnInputAsync는 위 예제와 동일한 방식으로 구현
/// }
/// </code>
/// </example>
public interface IFlowNode
{
    /// <summary>이 노드 인스턴스의 고유 식별자(플로우 내에서 유일). <see cref="NodeConfig.Id"/>와 동일한 값입니다.</summary>
    string Id { get; }

    /// <summary>노드 타입 이름(예: <c>"inject"</c>, <c>"function"</c>). <see cref="NodeConfig.Type"/>과 동일합니다.</summary>
    string Type { get; }

    /// <summary>캔버스에 표시되는 이름. 배포 후에도 이름 변경이 가능해 get/set 모두 노출합니다.</summary>
    string Name { get; set; }

    /// <summary>이 노드가 가진 입력 포트 목록(대부분 0개 또는 1개).</summary>
    IReadOnlyList<NodePort> InputPorts { get; }

    /// <summary>이 노드가 가진 출력 포트 목록(Switch 노드처럼 여러 개일 수 있음).</summary>
    IReadOnlyList<NodePort> OutputPorts { get; }

    /// <summary>배포(<c>DeployAsync</c>) 시 인스턴스 생성 직후 1회 호출됩니다. 연결 초기화·이벤트 구독 등을 수행합니다.</summary>
    Task OnStartAsync(INodeContext ctx, CancellationToken ct);

    /// <summary>이 노드의 입력 포트로 <see cref="Msg"/>가 들어올 때마다 호출됩니다. 노드의 실제 동작(변환·라우팅 등)이 여기서 일어납니다.</summary>
    Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct);

    /// <summary>재배포 또는 종료 시 1회 호출됩니다. <see cref="OnStartAsync"/>에서 구독한 이벤트·연결을 여기서 해제해야 합니다(공통 규칙 ②).</summary>
    Task OnCloseAsync(INodeContext ctx);

    /// <summary>
    /// (★ RT-06) 이 노드 타입이 동시에 처리할 수 있는 최대 <see cref="OnInputAsync"/> 호출 수의
    /// 코드 레벨 기본값입니다(05번 탭 카드3). 기본값 1은 노드 내부 상태(커넥션·카운터 등)를 순차
    /// 처리로 안전하게 지키고, HTTP 요청처럼 진짜 비동기 I/O를 쓰는 노드는 이 멤버를 재정의해 상향할
    /// 수 있습니다. 실제 배포에서는 사용자가 Editor에서 지정한 <see cref="NodeConfig.MaxConcurrency"/>가
    /// 이 기본값보다 우선합니다 — 이 멤버는 배포 정보를 찾을 수 없을 때의 대체값입니다.
    /// </summary>
    int MaxConcurrency => 1;
}
