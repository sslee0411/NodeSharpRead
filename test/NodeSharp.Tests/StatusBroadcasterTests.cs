using Microsoft.AspNetCore.SignalR;
using NodeSharp.Contracts.Events;
using NodeSharp.Runner.Core;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="StatusBroadcaster"/>(LK-02a)에 대한 테스트입니다. 실제 <see cref="IHubContext{THub}"/>는
/// 살아있는 SignalR 연결(Kestrel/TestServer)이 있어야 얻을 수 있어, 이 파일은 <see cref="IHubContext{THub}"/>·
/// <see cref="IHubClients"/>·<see cref="IClientProxy"/> 3개 인터페이스를 손으로 구현한 가짜(Moq 없이,
/// 이 저장소가 이미 쓰는 "손으로 구현한 가짜" 관례 — <c>FakeNodeContext</c>/<c>RecordingNodeContext</c>와
/// 동일한 스타일)로 <c>Clients.All.SendAsync(...)</c> 호출을 그대로 기록해 검증합니다. 실제 SignalR
/// 엔드포인트 기동·클라이언트 연결(TestServer + 실제 HubConnection)까지의 end-to-end 검증은 이
/// Step(LK-02a)의 xUnit 범위 밖입니다(RN-04a의 /health와 동일하게 "실제 실행 확인" 영역 — Program.cs
/// XML 문서 참고).
/// </summary>
public class StatusBroadcasterTests
{
    /// <summary><c>Clients.All.SendAsync(method, evt)</c> 호출을 그대로 기록하는 가짜 <see cref="IClientProxy"/>.</summary>
    private sealed class FakeClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = new();
        public bool ThrowOnSend { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return ThrowOnSend
                ? Task.FromException(new InvalidOperationException("테스트용 강제 실패 — SignalR 전송 예외 격리 검증용"))
                : Task.CompletedTask;
        }
    }

    /// <summary>
    /// <see cref="All"/>만 실제로 쓰는 가짜 <see cref="IHubClients"/> — 나머지 멤버는 이 클래스의 테스트
    /// 범위 밖이라 호출하지 않는다. <c>Client(string)</c>은 ASP.NET Core 7.0부터 <see cref="IHubClients"/>가
    /// 반환 타입을 <c>ISingleClientProxy</c>로 좁혀 <c>new</c>로 가린 멤버라(하위 호환 주석 참고),
    /// 기반 인터페이스 <c>IHubClients&lt;IClientProxy&gt;</c>의 원래 멤버는 명시적 구현으로, 가려진
    /// 새 멤버는 공개 메서드로 각각 따로 구현한다.
    /// </summary>
    private sealed class FakeHubClients : IHubClients
    {
        public FakeClientProxy AllProxy { get; } = new();
        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    /// <summary><see cref="Clients"/>만 실제로 쓰는 가짜 <see cref="IHubContext{THub}"/> — <see cref="Groups"/>는 이 클래스의 테스트 범위 밖.</summary>
    private sealed class FakeHubContext : IHubContext<MonitorHub>
    {
        public FakeHubClients ClientsFake { get; } = new();
        public IHubClients Clients => ClientsFake;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    [Fact]
    public void Subscribe는_4가지_이벤트를_각각_올바른_메서드_이름으로_중계한다()
    {
        var hubContext = new FakeHubContext();
        var broadcaster = new StatusBroadcaster(hubContext);
        var bus = new EventBusAdapter(new EventBus());   // 다른 테스트와 구독이 섞이지 않도록 독립 인스턴스
        using var sub = broadcaster.Subscribe(bus);

        var at = DateTime.UtcNow;
        bus.Publish(new NodeStatusEvent("n1", "green", "dot", "정상", at));
        bus.Publish(new FlowActivityEvent("n1", 0, "n2", "m1", at));
        bus.Publish(new DebugMessageEvent("n3", "디버그", "{}", at));
        // (LK-04) NodeErrorEvent가 노드정보·예외타입·msg 스냅샷을 담도록 확장(FlowEngine.DispatchOneAsync,
        // 03번 Step맵 LK-04) — 이 테스트는 StatusBroadcaster가 필드 내용과 무관하게 "nodeError" 메서드로
        // 그대로 중계하는지만 보므로 값 자체는 더미로 채운다.
        bus.Publish(new NodeErrorEvent(
            NodeId: "n1",
            NodeName: "테스트노드",
            NodeType: "function",
            ExceptionType: "InvalidOperationException",
            Message: "실패",
            StackTrace: null,
            MsgId: "m1",
            MsgSnapshotJson: "{}",
            At: at));

        var sent = hubContext.ClientsFake.AllProxy.Sent;
        Assert.Equal(4, sent.Count);
        Assert.Contains(sent, s => s.Method == "nodeStatus" && s.Args[0] is NodeStatusEvent);
        Assert.Contains(sent, s => s.Method == "flowActivity" && s.Args[0] is FlowActivityEvent);
        Assert.Contains(sent, s => s.Method == "debugMessage" && s.Args[0] is DebugMessageEvent);
        Assert.Contains(sent, s => s.Method == "nodeError" && s.Args[0] is NodeErrorEvent);
    }

    [Fact]
    public void Dispose하면_이후_이벤트는_더_이상_중계되지_않는다()
    {
        var hubContext = new FakeHubContext();
        var broadcaster = new StatusBroadcaster(hubContext);
        var bus = new EventBusAdapter(new EventBus());
        var sub = broadcaster.Subscribe(bus);

        bus.Publish(new NodeStatusEvent("n1", "green", "dot", "정상", DateTime.UtcNow));
        sub.Dispose();
        bus.Publish(new NodeStatusEvent("n1", "red", "ring", "에러", DateTime.UtcNow));

        Assert.Single(hubContext.ClientsFake.AllProxy.Sent);
    }

    [Fact]
    public void SendAsync가_실패해도_Publish_호출부에는_예외가_전파되지_않는다()
    {
        // (LK-02a) StatusBroadcaster.cs 클래스 remarks의 "SignalR 전송 예외 격리" 항목을 직접 검증 —
        // FlowEngine/NodeContext의 Publish 호출부가 SignalR 실패로 인해 죽으면 안 된다.
        var hubContext = new FakeHubContext();
        hubContext.ClientsFake.AllProxy.ThrowOnSend = true;
        var broadcaster = new StatusBroadcaster(hubContext);
        var bus = new EventBusAdapter(new EventBus());
        using var sub = broadcaster.Subscribe(bus);

        var ex = Record.Exception(() => bus.Publish(new NodeStatusEvent("n1", "red", "ring", "에러", DateTime.UtcNow)));

        Assert.Null(ex);
        Assert.Single(hubContext.ClientsFake.AllProxy.Sent);   // 실패했어도 전송 시도 자체는 기록됨
    }

    [Fact]
    public void 서로_다른_EventBus를_구독하면_각각_독립적으로_중계된다()
    {
        // Subscribe(IEventBus)를 여러 번 호출해도(예: 서로 다른 FlowEngine 인스턴스) 섞이지 않는지 확인.
        var hubContext = new FakeHubContext();
        var broadcaster = new StatusBroadcaster(hubContext);
        var busA = new EventBusAdapter(new EventBus());
        var busB = new EventBusAdapter(new EventBus());
        using var subA = broadcaster.Subscribe(busA);
        using var subB = broadcaster.Subscribe(busB);

        busA.Publish(new NodeStatusEvent("a1", "green", "dot", "A", DateTime.UtcNow));
        busB.Publish(new NodeStatusEvent("b1", "green", "dot", "B", DateTime.UtcNow));

        // 같은 hub로 중계되므로(전역 IHubContext) 총 2건 — 이 테스트는 "구독 자체가 죽지 않고 둘 다
        // 정상 동작"만 확인한다(같은 hub를 여러 엔진이 공유하는 것은 의도된 설계, 위 클래스 remarks 참고).
        Assert.Equal(2, hubContext.ClientsFake.AllProxy.Sent.Count);
    }
}
