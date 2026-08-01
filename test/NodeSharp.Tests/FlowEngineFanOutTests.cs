using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.RouteAsync"/>의 Fan-out 순차/병렬 하이브리드(RT-04b, 02번 설계 문서 5번 탭
/// 카드1)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-04b): A→(B,C,D) Fan-out에서 B/C/D가
/// 각각 다른 <see cref="Msg"/> 인스턴스를 받아, 한쪽에서 <c>Payload</c>를 바꿔도 다른 쪽에 영향이
/// 없는지 확인.
/// </summary>
public class FlowEngineFanOutTests
{
    /// <summary>입력을 받아 순서·Payload를 정적 로그에 기록하는 테스트 전용 수신 노드.</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public static readonly List<string> ReceivedOrder = new();
        public static readonly Dictionary<string, Msg> ReceivedMsgByName = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "receiver";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            lock (ReceivedOrder)
            {
                ReceivedOrder.Add(Name);
                ReceivedMsgByName[Name] = msg;
            }
            return Task.CompletedTask;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>발신 전용 더미 노드 — RouteAsync는 엔진 API로 직접 호출하므로 OnInputAsync는 쓰이지 않음.</summary>
    private sealed class SourceNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "source";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("source", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(SourceNode));
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        return new FlowEngine(registry);
    }

    private static FlowDefinition BuildFanOutFlow(DispatchMode dispatch) =>
        new(
            Id: "f1", Name: "Fan-out 테스트",
            Nodes: new[]
            {
                new NodeConfig("n1", "source", "발신", "f1", new Dictionary<string, object?>(), OutputDispatch: dispatch),
                new NodeConfig("n2", "receiver", "B", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n3", "receiver", "C", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n4", "receiver", "D", "f1", new Dictionary<string, object?>()),
            },
            Wires: new[]
            {
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0),
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n4", TargetPort: 0),
            });

    [Fact]
    public async Task Sequential_기본값은_Wire_순서대로_하나씩_전달한다()
    {
        ReceiverNode.ReceivedOrder.Clear();
        ReceiverNode.ReceivedMsgByName.Clear();
        var engine = BuildEngine();
        var flow = BuildFanOutFlow(DispatchMode.Sequential);
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "알람" }, CancellationToken.None);

        Assert.Equal(new[] { "B", "C", "D" }, ReceiverNode.ReceivedOrder);
    }

    [Fact]
    public async Task Parallel_모드는_B_C_D_모두에게_전달된다()
    {
        ReceiverNode.ReceivedOrder.Clear();
        ReceiverNode.ReceivedMsgByName.Clear();
        var engine = BuildEngine();
        var flow = BuildFanOutFlow(DispatchMode.Parallel);
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "알람" }, CancellationToken.None);

        Assert.Equal(new HashSet<string> { "B", "C", "D" }, ReceiverNode.ReceivedOrder.ToHashSet());
        Assert.Equal(3, ReceiverNode.ReceivedOrder.Count);
    }

    [Theory]
    [InlineData(DispatchMode.Sequential)]
    [InlineData(DispatchMode.Parallel)]
    public async Task 분기마다_서로_다른_Msg_인스턴스를_받아_Payload_변경이_격리된다(DispatchMode dispatch)
    {
        ReceiverNode.ReceivedOrder.Clear();
        ReceiverNode.ReceivedMsgByName.Clear();
        var engine = BuildEngine();
        var flow = BuildFanOutFlow(dispatch);
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "원본" }, CancellationToken.None);

        // 세 노드가 받은 Msg는 서로 다른 인스턴스(Clone)여야 한다 — 하나를 바꿔도 나머지는 영향받지 않음
        var msgB = ReceiverNode.ReceivedMsgByName["B"];
        var msgC = ReceiverNode.ReceivedMsgByName["C"];
        var msgD = ReceiverNode.ReceivedMsgByName["D"];
        Assert.NotSame(msgB, msgC);
        Assert.NotSame(msgB, msgD);
        Assert.NotSame(msgC, msgD);

        msgB.Payload = "B에서_변경";

        Assert.Equal("B에서_변경", msgB.Payload);
        Assert.Equal("원본", msgC.Payload);
        Assert.Equal("원본", msgD.Payload);
    }

    [Fact]
    public async Task OutputDispatch를_지정하지_않으면_기본값_Sequential로_동작한다()
    {
        ReceiverNode.ReceivedOrder.Clear();
        ReceiverNode.ReceivedMsgByName.Clear();
        var engine = BuildEngine();
        // NodeConfig.OutputDispatch 기본값(Sequential)을 그대로 사용 — 명시적으로 지정하지 않음
        var flow = new FlowDefinition(
            Id: "f1", Name: "기본값 테스트",
            Nodes: new[]
            {
                new NodeConfig("n1", "source", "발신", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "receiver", "B", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n3", "receiver", "C", "f1", new Dictionary<string, object?>()),
            },
            Wires: new[]
            {
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0),
            });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "알람" }, CancellationToken.None);

        Assert.Equal(new[] { "B", "C" }, ReceiverNode.ReceivedOrder);
    }
}
