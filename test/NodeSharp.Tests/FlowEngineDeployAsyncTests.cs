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
/// ★ RT-02a: 등록되지 않은 타입 처리 테스트는 "예외 전파"에서 "MissingNode로 대체, 배포는 계속 성공"으로
/// 갱신했다 — RT-01b 시점엔 MissingNode 대체가 아직 없어 예외 전파가 정답이었지만(그 자체가 올바른
/// 범위 한정이었음), RT-02a가 명시적으로 이 동작을 바꾸는 것이 이 Step의 목적이다.
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
    public async Task DeployAsync는_등록되지_않은_타입이_섞여있어도_예외_없이_완료되고_MissingNode가_대신_배포된다()
    {
        OrderTrackingNode.CallLog.Clear();
        var engine = BuildEngineWithOrderTrackingType();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[]
            {
                new NodeConfig("n1", "order-tracking", "노드1", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "no-such-type", "노드2", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n3", "order-tracking", "노드3", "f1", new Dictionary<string, object?>()),
            },
            Wires: Array.Empty<Wire>());

        // ★ RT-02a: 예외 없이 완료되어야 한다(RT-01b 때와 달라진 부분)
        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.IsType<OrderTrackingNode>(engine.Nodes["n1"]);
        Assert.IsType<MissingNode>(engine.Nodes["n2"]);
        Assert.IsType<OrderTrackingNode>(engine.Nodes["n3"]);
        // MissingNode는 OnStartAsync를 건너뛰므로 로그에는 정상 노드 2개(n1/n3)의 start만 남는다
        Assert.Equal(
            new[] { "create", "create", "start:노드1", "start:노드3" },
            OrderTrackingNode.CallLog);
    }
}
