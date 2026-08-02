namespace NodeSharp.Contracts.Interfaces;

// 한글명: 공유 서비스 노드 계약
/// <summary>
/// 여러 노드가 하나의 실제 리소스(TCP 서버, DB 커넥션 등)를 공유할 때 그 리소스의 생명주기를
/// 나타내는 계약입니다. Node-RED의 "config node" 개념에 대응합니다.
/// 설계 근거: 02번 문서 2번 탭 카드 6.
/// </summary>
/// <remarks>
/// 실제 참조 카운트 관리(몇 개의 노드가 참조 중인지, 마지막 참조가 해제될 때만 <see cref="StopAsync"/>를
/// 호출)는 이 인터페이스가 아니라 <c>RT-10</c>의 <c>SharedResourceManager</c>가 담당합니다 —
/// 이 인터페이스는 "시작/종료" 두 지점만 정의합니다.
/// </remarks>
/// <example>
/// <code>
/// // 여러 TagNode가 참조하는 TCP 서버 — 최초 참조 시에만 Start, 마지막 참조 해제 시에만 Stop
/// public sealed class TcpServerNode : ISharedServiceNode
/// {
///     public string Id { get; init; } = default!;   // 예: "srv-5000" — 같은 포트 설정이면 항상 동일 Id
///     private System.Net.Sockets.TcpListener? _listener;
///
///     public Task StartAsync(CancellationToken ct)
///     {
///         _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, 5000);
///         _listener.Start();
///         return Task.CompletedTask;
///     }
///
///     public Task StopAsync() { _listener?.Stop(); return Task.CompletedTask; }
/// }
///
/// // SharedResourceManager(RT-10)가 참조 카운트로 이 생명주기를 관리하는 방식(개념 예시)
/// // var server = await sharedResourceManager.AcquireAsync("srv-5000", () => new TcpServerNode { Id = "srv-5000" }, ct);
/// // ... 노드가 배포에서 제거되면 ...
/// // await sharedResourceManager.ReleaseAsync("srv-5000");   // 참조가 0이 될 때만 실제 StopAsync 호출
/// </code>
/// </example>
public interface ISharedServiceNode
{
    /// <summary>이 공유 리소스의 식별자. 같은 설정(예: 같은 포트)을 가리키는 노드는 항상 동일한 Id를 가져야 참조 카운트가 하나로 합산됩니다.</summary>
    string Id { get; }

    /// <summary>이 리소스를 처음 참조하는 노드가 배포될 때 1회만 호출됩니다(실제 연결 오픈 등).</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>이 리소스를 참조하던 마지막 노드가 배포에서 사라질 때 1회만 호출됩니다(실제 연결 종료 등).</summary>
    Task StopAsync();
}
