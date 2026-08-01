using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.DeployAsync"/>(RT-01b, Full 모드, 02번 설계 문서 2번 탭 카드4 — Phase 2)에 대한
/// 단위 테스트입니다. 완료 기준: 노드 3개 이상인 FlowDefinition을 배포하면 모든 노드가
/// CreateInstance→OnStartAsync 순으로, 순서가 뒤바뀌지 않고 호출되는지 확인.
/// </summary>
public class FlowEngineDeployAsyncTests
{
    /// <summary>생성/기동 호출 순서를 정적 로그에 기록하는 테스트 전용 노드.</summary>
    private sealed class OrderTrackingNode : IFlowNode
    {
        public static readonly List<string> CallLog = new();

        public OrderTrackingNode() => CallLog.Add("create");

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "order-tracking";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
        {
            CallLog.Add($"start:{Name}");
            return Task.CompletedTask;
        }

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngineWithOrderTrackingType()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("order-tracking", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(OrderTrackingNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public async Task DeployAsync는_노드_3개_전체를_CreateInstance한_후에야_OnStartAsync를_순서대로_호출한다()
    {
        OrderTrackingNode.CallLog.Clear();
        var engine = BuildEngineWithOrderTrackingType();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[]
            {
                new NodeConfig("n1", "order-tracking", "노드1", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "order-tracking", "노드2", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n3", "order-tracking", "노드3", "f1", new Dictionary<string, object?>()),
            },
            Wires: Array.Empty<Wire>());

        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.Equal(
            new[] { "create", "create", "create", "start:노드1", "start:노드2", "start:노드3" },
            OrderTrackingNode.CallLog);
    }

    [Fact]
    public async Task DeployAsync는_배포된_노드를_NodeConfig_Id로_조회할_수_있게_Nodes에_채운다()
    {
        OrderTrackingNode.CallLog.Clear();
        var engine = BuildEngineWithOrderTrackingType();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { new NodeConfig("n1", "order-tracking", "노드1", "f1", new Dictionary<string, object?>()) },
            Wires: Array.Empty<Wire>());

        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.True(engine.Nodes.ContainsKey("n1"));
        Assert.IsType<OrderTrackingNode>(engine.Nodes["n1"]);
    }

    [Fact]
    public async Task DeployAsync는_등록되지_않은_타입이_섞여있으면_예외를_전파한다()
    {
        OrderTrackingNode.CallLog.Clear();
        var engine = BuildEngineWithOrderTrackingType();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[]
            {
                new NodeConfig("n1", "order-tracking", "노드1", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "no-such-type", "노드2", "f1", new Dictionary<string, object?>()),
            },
            Wires: Array.Empty<Wire>());

        // RT-01b는 예외 격리(RT-02a/b)를 아직 구현하지 않으므로 그대로 전파되어야 한다
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.DeployAsync(flow, CancellationToken.None));
    }
}
