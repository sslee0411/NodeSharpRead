using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <see cref="FlowEngine.TriggerManualAsync"/>에
/// 대한 단위 테스트입니다. 완료 기준: ① <see cref="IManuallyTriggerable"/>을 구현하는 배포된 노드를
/// nodeId로 트리거하면 그 노드의 <c>TriggerAsync</c>가 호출되고(실제로 와이어를 타고 다음 노드까지
/// 전달됨) ② 존재하지 않는 nodeId ③ <see cref="IManuallyTriggerable"/>을 구현하지 않는 노드 Id 둘 다
/// 예외 없이 조용히 무시되는지 확인.
/// </summary>
public class FlowEngineTriggerManualAsyncTests
{
    /// <summary><see cref="InjectNode"/>를 흉내 낸 테스트 전용 소스 노드 — 입력 포트 없이 <see cref="IManuallyTriggerable"/>만 구현.</summary>
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

    /// <summary>입력을 정적 로그에 기록만 하는 테스트 전용 수신 노드 — TestTriggerNode와 동일하게 <see cref="IManuallyTriggerable"/>은 구현하지 않아 "미구현 노드" 케이스로도 재사용한다.</summary>
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

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("test-trigger", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(TestTriggerNode));
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        // (FlowEngineRouteAsyncTests의 v2.99 버그 수정과 동일한 이유) 테스트마다 독립된 EventBus를
        // 명시적으로 주입해 다른 테스트 파일과 격리한다.
        return new FlowEngine(registry, eventBus: new EventBusAdapter(new EventBus()));
    }

    [Fact]
    public async Task TriggerManualAsync는_IManuallyTriggerable_구현_노드를_트리거해_와이어로_전달한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "test-trigger", "트리거", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, CancellationToken.None);

        await engine.TriggerManualAsync("n1", payload: "수동 발동", CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("수동 발동", ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task TriggerManualAsync는_존재하지_않는_nodeId면_예외_없이_조용히_무시한다()
    {
        var engine = BuildEngine();
        var flow = new FlowDefinition(Id: "f1", Name: "빈 플로우", Nodes: Array.Empty<NodeConfig>(), Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow, CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => engine.TriggerManualAsync("없는-id", null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task TriggerManualAsync는_IManuallyTriggerable을_구현하지_않는_노드면_조용히_무시한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우", Nodes: new[] { n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow, CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => engine.TriggerManualAsync("n2", null, CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(ReceiverNode.Received);
    }

    [Fact]
    public async Task TriggerManualAsync가_넘긴_payload가_그대로_Msg_Payload에_담긴다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "test-trigger", "트리거", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, CancellationToken.None);

        await engine.TriggerManualAsync("n1", payload: 42, CancellationToken.None);

        Assert.Equal(42, ReceiverNode.Received[0]);
    }
}
