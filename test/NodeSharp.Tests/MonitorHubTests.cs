using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runner.Core;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <see cref="MonitorHub.TriggerInject"/>에
/// 대한 단위 테스트입니다. 완료 기준: ① <see cref="CurrentEngineHolder.Engine"/>에 배포된 엔진이 있으면
/// 그 엔진의 <c>TriggerManualAsync</c>로 위임되는지(실제로 와이어를 타고 전달됨) ② 아직 배포된 적이
/// 없으면(<c>Engine</c>이 <c>null</c>) 예외 없이 조용히 무시되는지 확인. <c>Hub.Context</c>에 의존하지
/// 않도록 설계돼(MonitorHub.cs 자체 문서 참고) SignalR 커넥션 없이 <c>new MonitorHub(holder)</c>를
/// 직접 생성해 테스트할 수 있습니다.
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
        var hub = new MonitorHub(holder);

        await hub.TriggerInject("n1");

        Assert.Single(ReceiverNode.Received);
    }

    [Fact]
    public async Task TriggerInject는_아직_배포된_엔진이_없으면_예외_없이_조용히_무시한다()
    {
        var holder = new CurrentEngineHolder(); // Engine == null
        var hub = new MonitorHub(holder);

        var exception = await Record.ExceptionAsync(() => hub.TriggerInject("아무거나"));

        Assert.Null(exception);
    }
}
