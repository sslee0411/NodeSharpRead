using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runner.Core;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <see cref="MonitorHub.TriggerInject"/>에
/// 대한 단위 테스트입니다. 완료 기준: ① <see cref="CurrentEngineHolder.Engine"/>에 배포된 엔진이 있으면
/// 그 엔진의 <c>TriggerManualAsync</c>로 위임되는지(실제로 와이어를 타고 전달됨) ② 아직 배포된 적이
/// 없으면(<c>Engine</c>이 <c>null</c>) 예외 없이 조용히 무시되는지 확인. <c>Hub.Context</c>에 의존하지
/// 않도록 설계돼(MonitorHub.cs 자체 문서 참고) SignalR 커넥션 없이
/// <c>new MonitorHub(holder, tokenStore, msgTraceStore, simulationSlaveHolder)</c>를 직접 생성해
/// 테스트할 수 있습니다.
/// (LK-03) 생성자가 <see cref="RunnerTokenStore"/>도 받도록 바뀌어 아래 테스트들이 더미 인스턴스를
/// 하나씩 추가로 넘깁니다. <see cref="MonitorHub.ReissueToken"/> 자체는 여기서 단위 테스트하지
/// 않습니다 — 내부에서 <c>Clients.Others.SendAsync(...)</c>를 호출하는데, <c>Hub.Clients</c>는
/// 실제 SignalR 파이프라인이 인스턴스를 관리할 때만 채워지는 프로퍼티라 이렇게 직접 생성한
/// 인스턴스로 호출하면 <see cref="NullReferenceException"/>이 납니다(<see cref="TriggerInject"/>가
/// <c>Hub.Context</c> 대신 <c>CancellationToken.None</c>을 쓰는 것과 같은 이유의 반대 사례 — 이번엔
/// 피할 방법이 없어 애초에 그 부분만 xUnit 범위 밖으로 남겨둠, LK-02a의 "실제 SignalR 엔드포인트
/// 기동·연결 자체는 실제 실행 확인 영역" 선례와 동일). 토큰 교체 로직 자체(값 생성·파일 저장·
/// 이전 값 무효화)는 <c>RunnerTokenStoreTests.cs</c>가 <see cref="RunnerTokenStore"/>를 직접
/// 단위 테스트합니다.
/// (LK-04) 생성자가 <see cref="MsgTraceStore"/>도 받도록 바뀌어 위 두 테스트가 더미 인스턴스를 하나씩
/// 더 넘깁니다. <see cref="MonitorHub.GetMsgTrace"/>는 <c>Hub.Clients</c>/<c>Hub.Context</c>에 전혀
/// 의존하지 않아(<see cref="MsgTraceStore.GetTrace"/>로 그대로 위임만 함) <see cref="ReissueToken"/>과
/// 달리 직접 생성한 인스턴스로도 안전하게 단위 테스트할 수 있습니다 — 아래
/// <see cref="GetMsgTrace는_MsgTraceStore로_그대로_위임한다"/> 참고. 누적 로직 자체(FlowActivityEvent
/// 구독·상한 초과 시 오래된 것부터 제거)는 <c>MsgTraceStoreTests.cs</c>가 <see cref="MsgTraceStore"/>를
/// 직접 단위 테스트합니다.
/// (PD-01e) 생성자가 <see cref="SimulationSlaveHolder"/>도 받도록 바뀌어 아래 모든 테스트가 더미
/// 인스턴스를 하나씩 더 넘깁니다. <see cref="MonitorHub.SetSimulatedRegister"/> 자체는 여기서 단위
/// 테스트하지 않습니다 — 순수하게 <see cref="SimulationSlaveHolder.TryGet"/> 위임만 하는 얇은
/// 메서드라(<see cref="MonitorHub.GetMsgTrace"/>와 동일한 성격) 직접 생성한 인스턴스로도 안전하게
/// 호출할 수는 있지만, 이 테스트 파일의 범위(TriggerInject/ReissueToken/GetMsgTrace)를 넘어서는
/// 별도 관심사라 추가하지 않았습니다.
/// </summary>
public class MonitorHubTests
{
    private sealed class TestTriggerNode : IFlowNode, IManuallyTriggerable
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "test-trigger";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;

        public Task TriggerAsync(object? payload, INodeContext ctx, CancellationToken ct) =>
            ctx.RouteAsync(Id, 0, new Msg { Payload = payload }, ct);
    }

    private sealed class ReceiverNode : IFlowNode
    {
        public static readonly List<object?> Received = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "receiver";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            Received.Add(msg.Payload);
            return Task.CompletedTask;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public async Task TriggerInject는_배포된_엔진의_TriggerManualAsync로_위임한다()
    {
        ReceiverNode.Received.Clear();
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("test-trigger", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(TestTriggerNode));
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[]
            {
                new NodeConfig("n1", "test-trigger", "트리거", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>()),
            },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, CancellationToken.None);

        var holder = new CurrentEngineHolder { Engine = engine };
        var hub = new MonitorHub(holder, new RunnerTokenStore(), new MsgTraceStore(), new SimulationSlaveHolder());

        await hub.TriggerInject("n1");

        Assert.Single(ReceiverNode.Received);
    }

    [Fact]
    public async Task TriggerInject는_아직_배포된_엔진이_없으면_예외_없이_조용히_무시한다()
    {
        var holder = new CurrentEngineHolder(); // Engine == null
        var hub = new MonitorHub(holder, new RunnerTokenStore(), new MsgTraceStore(), new SimulationSlaveHolder());

        var exception = await Record.ExceptionAsync(() => hub.TriggerInject("아무거나"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetMsgTrace는_MsgTraceStore로_그대로_위임한다()
    {
        // ★ EventBus 테스트 격리 원칙(LK-02a 착수 중 발견) — 기본 생성자는 프로세스 전역
        // EventBus.Instance를 감싸므로, 다른 테스트와 구독이 섞이지 않도록 매번 새 EventBus를 주입한다.
        var eventBus = new EventBusAdapter(new EventBus());
        var msgTraceStore = new MsgTraceStore();
        using var subscription = msgTraceStore.Subscribe(eventBus);
        eventBus.Publish(new FlowActivityEvent("inject-1", 0, "function-1", "msg-1", DateTime.UtcNow));

        var hub = new MonitorHub(new CurrentEngineHolder(), new RunnerTokenStore(), msgTraceStore, new SimulationSlaveHolder());
        var trace = await hub.GetMsgTrace("msg-1");

        Assert.NotNull(trace);
        Assert.Single(trace!.Steps);
        Assert.Equal("inject-1", trace.Steps[0].FromNodeId);
        Assert.Equal("function-1", trace.Steps[0].ToNodeId);
    }

    [Fact]
    public async Task GetMsgTrace는_추적된_적_없는_msgId면_null을_반환한다()
    {
        var hub = new MonitorHub(new CurrentEngineHolder(), new RunnerTokenStore(), new MsgTraceStore(), new SimulationSlaveHolder());

        var trace = await hub.GetMsgTrace("존재하지-않는-msgId");

        Assert.Null(trace);
    }
}
