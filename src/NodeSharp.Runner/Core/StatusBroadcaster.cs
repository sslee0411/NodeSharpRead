using Microsoft.AspNetCore.SignalR;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 상태 브로드캐스터
/// 역활 및 기능 : FlowEngine의 EventBus에 발행되는 4가지 모니터링 이벤트를 SignalR Hub로 그대로 중계하는 클래스
///
/// (LK-02a) 02번 설계 문서 7번 탭 카드2 <c>StatusBroadcaster</c>가 그대로 — <see cref="IEventBus"/>에
/// 발행되는 <see cref="NodeStatusEvent"/>/<see cref="FlowActivityEvent"/>/<see cref="DebugMessageEvent"/>/
/// <see cref="NodeErrorEvent"/> 4종을 구독해 <see cref="IHubContext{THub}"/>(<see cref="MonitorHub"/>)로
/// <c>Clients.All.SendAsync(...)</c>합니다. <c>NodeStatusConsoleLogger</c>(RN-02, "IEventBus를
/// 구독해 한 가지 형태로만 내보내는 얇은 구독자")와 동일한 성격의 클래스이며, 콘솔 대신 SignalR로
/// 내보낸다는 점만 다릅니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>구독 시점 — <c>FlowDeployer.CreateEngineWithLogger</c></b>: 이 클래스 자신은 <em>어떤</em>
/// <see cref="IEventBus"/>를 구독할지 모릅니다(생성자는 <see cref="IHubContext{THub}"/>만 받음) —
/// <c>Subscribe(IEventBus)</c>를 호출하는 쪽이 대상을 정합니다. <c>NodeSharp.Runner.Worker</c>가
/// <c>FlowDeployer.DeployIfAvailableAsync</c>/<c>RedeployAsync</c>에 <c>attachMonitor</c> 콜백으로
/// <c>eventBus =&gt; broadcaster.Subscribe(eventBus)</c>를 넘기면, <c>FlowDeployer.CreateEngineWithLogger</c>가
/// "진짜 새 <see cref="FlowEngine"/>을 만들 때만" 그 콜백을 호출합니다 — LK-01이 재배포 시 같은 엔진
/// 인스턴스를 재사용하도록 설계했으므로(엔진 재사용 시 새로 구독하면 같은 이벤트가 중복 전송됨),
/// 이 클래스가 스스로 "이미 구독했는지"를 추적할 필요가 없습니다(호출 지점 자체가 1엔진당 1회로
/// 보장됨).</item>
/// <item><b>왜 <c>IEventBus</c>(Contracts 계약)가 아니라 <c>NodeSharp.Util.Messaging.EventBus</c>(구체 클래스)로 확장하지
/// 않았는가</b>: 설계 문서 원본 스니펫은 <c>SubscribeAsync</c>(비동기 핸들러)를 쓰면 더 자연스럽지만,
/// <see cref="IEventBus"/> 계약에는 <c>Subscribe(Action&lt;TEvent&gt;)</c>(동기)만 있습니다 — 계약을
/// 넓히려면 <c>NR-04</c>/<c>NR-11</c> 선례처럼 기존 구현체 전부(<c>NodeContext</c> + 테스트 스텁
/// 5곳)를 함께 고쳐야 해 이번 Step 범위를 벗어난다고 판단, 기존 동기 <c>Subscribe</c>로 충분히
/// 구현했습니다(아래 <see cref="Broadcast{TEvent}"/> 참고 — SignalR 전송 자체는 비동기지만, "핸들러가
/// 그 Task를 기다리지 않고 바로 반환"하는 방식으로 우회).</item>
/// <item><b>SignalR 전송 예외 격리</b>: <c>IClientProxy.SendAsync</c>가 실패해도(예: 클라이언트
/// 연결 끊김 직후 경합) 그 예외가 <see cref="IEventBus.Publish{TEvent}"/> 호출부(<c>FlowEngine</c>·
/// <c>NodeContext</c>)까지 거슬러 올라가 플로우 실행을 멈추면 안 됩니다 — <see cref="Broadcast{TEvent}"/>가
/// Task를 <c>await</c>하지 않고 <c>ContinueWith(..., OnlyOnFaulted)</c>로만 관찰해, 실패해도 콘솔에
/// 한 줄만 남기고 조용히 넘어갑니다(<c>FlowFileWatcher</c>의 "콜백 예외 격리"·<c>Worker</c>의
/// "단계별 격리"와 동일한 원칙).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Program.cs: builder.Services.AddSignalR(); builder.Services.AddSingleton&lt;StatusBroadcaster&gt;();
/// // Worker.cs:  attachMonitor: eventBus =&gt; _statusBroadcaster.Subscribe(eventBus)
/// //             (FlowDeployer.DeployIfAvailableAsync/RedeployAsync에 그대로 전달)
/// </code>
/// </example>
public sealed class StatusBroadcaster
{
    private readonly IHubContext<MonitorHub> _hub;

    /// <summary>DI(<c>AddSingleton&lt;StatusBroadcaster&gt;</c>)가 <c>AddSignalR()</c>로 등록된 <see cref="IHubContext{THub}"/>를 자동으로 주입합니다.</summary>
    public StatusBroadcaster(IHubContext<MonitorHub> hub) => _hub = hub;

    /// <summary>
    /// <paramref name="eventBus"/>에 발행되는 4가지 모니터링 이벤트를 구독해 SignalR로 중계합니다.
    /// 반환된 <see cref="IDisposable"/>을 <c>Dispose()</c>하면 4개 구독을 한 번에 해제합니다
    /// (<see cref="IEventBus"/> XML 문서의 "구독은 반드시 해제" 규칙).
    /// </summary>
    public IDisposable Subscribe(IEventBus eventBus)
    {
        var subscriptions = new List<IDisposable>
        {
            eventBus.Subscribe<NodeStatusEvent>(e => Broadcast("nodeStatus", e)),
            eventBus.Subscribe<FlowActivityEvent>(e => Broadcast("flowActivity", e)),
            eventBus.Subscribe<DebugMessageEvent>(e => Broadcast("debugMessage", e)),
            eventBus.Subscribe<NodeErrorEvent>(e => Broadcast("nodeError", e)),
        };
        return new CompositeSubscription(subscriptions);
    }

    /// <summary>
    /// <paramref name="method"/> 이름으로 연결된 모든 클라이언트에 <paramref name="evt"/>를 보냅니다.
    /// 위 클래스 remarks의 "SignalR 전송 예외 격리" 항목대로, 반환된 Task를 기다리지 않고 실패만
    /// 관찰합니다.
    /// </summary>
    private void Broadcast<TEvent>(string method, TEvent evt)
    {
        var task = _hub.Clients.All.SendAsync(method, evt);
        task.ContinueWith(
            t => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] SignalR 브로드캐스트({method}) 실패 — 다음 이벤트는 계속 전송을 시도합니다: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>여러 <see cref="IDisposable"/> 구독을 하나로 묶어 한 번에 해제하는 얇은 래퍼.</summary>
    private sealed class CompositeSubscription : IDisposable
    {
        private readonly List<IDisposable> _inner;
        public CompositeSubscription(List<IDisposable> inner) => _inner = inner;
        public void Dispose()
        {
            foreach (var d in _inner)
            {
                d.Dispose();
            }
        }
    }
}
