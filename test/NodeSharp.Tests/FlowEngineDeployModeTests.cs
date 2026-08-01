using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>(RT-03, 02번 설계
/// 문서 3번 탭 카드5)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-03): 같은 <see cref="FlowDefinition"/>에
/// <see cref="DeployMode"/> 4종을 각각 적용했을 때 실제 재배포 범위가 달라지고(어떤 노드 인스턴스가
/// 유지/재생성되는지) 로그로 구분되는지 확인.
/// </summary>
public class FlowEngineDeployModeTests
{
    /// <summary>생성/기동/종료를 정적 로그에 기록하는 테스트 전용 노드 — 재배포 범위 차이를 로그로 구분하는 데 사용.</summary>
    private sealed class TrackingNode : IFlowNode
    {
        public static readonly List<string> CallLog = new();

        public TrackingNode() => CallLog.Add("create");

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "tracking";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
        {
            CallLog.Add($"start:{Name}");
            return Task.CompletedTask;
        }

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnCloseAsync(INodeContext ctx)
        {
            CallLog.Add($"close:{Name}");
            return Task.CompletedTask;
        }
    }

    /// <summary>OnStartAsync에서 항상 예외를 던지는 노드 — FailedNodeIds가 매 배포마다 다시 계산되는지 확인용.</summary>
    private sealed class ThrowingOnStartNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "throwing-start";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => throw new InvalidOperationException("기동 실패 테스트");
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("tracking", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(TrackingNode));
        registry.TryRegister(new PluginManifest("throwing-start", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ThrowingOnStartNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public async Task ModifiedNodes_모드는_필드가_변경된_노드만_재생성하고_변경없는_노드는_인스턴스를_유지한다()
    {
        TrackingNode.CallLog.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "tracking", "노드1", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "tracking", "노드2", "f1", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        var originalN2 = engine.Nodes["n2"];

        var n1Changed = n1 with { Name = "노드1(변경됨)" };
        var flow2 = flow1 with { Nodes = new[] { n1Changed, n2 } };
        TrackingNode.CallLog.Clear();
        await engine.DeployAsync(flow2, DeployMode.ModifiedNodes, CancellationToken.None);

        // n1만 재생성(close→create→start), n2는 로그에 전혀 나타나지 않아야 한다(인스턴스 유지)
        Assert.Equal(new[] { "close:노드1", "create", "start:노드1(변경됨)" }, TrackingNode.CallLog);
        Assert.Same(originalN2, engine.Nodes["n2"]);
    }

    [Fact]
    public async Task ModifiedFlows_모드는_변경된_노드가_속한_Flow_탭_전체를_재시작한다()
    {
        TrackingNode.CallLog.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "tracking", "노드1", "flow-a", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "tracking", "노드2", "flow-a", new Dictionary<string, object?>());
        var n3 = new NodeConfig("n3", "tracking", "노드3", "flow-b", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2, n3 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        var originalN3 = engine.Nodes["n3"];

        var n1Changed = n1 with { Name = "노드1(변경됨)" };
        var flow2 = flow1 with { Nodes = new[] { n1Changed, n2, n3 } };
        TrackingNode.CallLog.Clear();
        await engine.DeployAsync(flow2, DeployMode.ModifiedFlows, CancellationToken.None);

        // n1만 필드가 바뀌었지만 같은 flow-a 소속인 n2도 함께 재시작되어야 한다. flow-b 소속 n3는 그대로 유지.
        Assert.Contains("close:노드1", TrackingNode.CallLog);
        Assert.Contains("close:노드2", TrackingNode.CallLog);
        Assert.DoesNotContain("close:노드3", TrackingNode.CallLog);
        Assert.Equal(2, TrackingNode.CallLog.Count(e => e == "create"));
        Assert.Same(originalN3, engine.Nodes["n3"]);
    }

    [Fact]
    public async Task RestartFlows_모드는_설정_변경이_없어도_전체_노드를_재시작한다()
    {
        TrackingNode.CallLog.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "tracking", "노드1", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var original = engine.Nodes["n1"];
        TrackingNode.CallLog.Clear();

        // 같은 FlowDefinition, 필드 변경 없음 — 그래도 RestartFlows는 재시작해야 한다
        await engine.DeployAsync(flow, DeployMode.RestartFlows, CancellationToken.None);

        Assert.Equal(new[] { "close:노드1", "create", "start:노드1" }, TrackingNode.CallLog);
        Assert.NotSame(original, engine.Nodes["n1"]);
    }

    [Fact]
    public async Task Full_모드로_재배포하면_새_FlowDefinition에서_사라진_노드는_닫힌_후_Nodes에서_제거된다()
    {
        TrackingNode.CallLog.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "tracking", "노드1", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "tracking", "노드2", "f1", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        var flow2 = flow1 with { Nodes = new[] { n1 } };   // n2 삭제
        TrackingNode.CallLog.Clear();
        await engine.DeployAsync(flow2, DeployMode.Full, CancellationToken.None);

        Assert.False(engine.Nodes.ContainsKey("n2"));
        Assert.Contains("close:노드2", TrackingNode.CallLog);
    }

    [Fact]
    public async Task FailedNodeIds는_매_배포마다_이번_배포에서_실제로_재시작한_노드_기준으로_다시_계산된다()
    {
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "throwing-start", "실패 노드", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "tracking", "정상 노드", "f1", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        Assert.Equal(new[] { "n1" }, engine.FailedNodeIds);

        // 아무 필드도 바뀌지 않은 채 ModifiedNodes로 재배포 — 재시작 대상이 없으므로 FailedNodeIds도 비워져야 한다
        await engine.DeployAsync(flow1, DeployMode.ModifiedNodes, CancellationToken.None);

        Assert.Empty(engine.FailedNodeIds);
    }

    [Fact]
    public async Task 최초_배포_전에는_ModifiedNodes로_배포해도_Full과_동일하게_전체_노드가_생성된다()
    {
        TrackingNode.CallLog.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "tracking", "노드1", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "tracking", "노드2", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());

        // _currentFlow가 아직 없는 상태(최초 배포) — ModifiedNodes라도 두 노드 모두 "추가됨"으로 취급되어야 한다
        await engine.DeployAsync(flow, DeployMode.ModifiedNodes, CancellationToken.None);

        Assert.True(engine.Nodes.ContainsKey("n1"));
        Assert.True(engine.Nodes.ContainsKey("n2"));
        Assert.Equal(2, TrackingNode.CallLog.Count(e => e == "create"));
    }
}
