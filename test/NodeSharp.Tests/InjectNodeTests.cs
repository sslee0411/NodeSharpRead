using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Inject;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="InjectNode"/>/<see cref="InjectNodeType"/>(NR-03a, 03번 개발 Step맵 Phase 7 — Inject
/// 노드의 첫 구현체)에 대한 통합 테스트입니다. 완료 기준(03번 Step맵 NR-03a): "Inject 버튼 클릭 시
/// 정확히 1회 Msg가 다음 노드로 전달되는지 확인" — Editor→Runner IPC(LK-02, Phase 8)가 아직 없어
/// 실제 WPF 클릭으로는 시연할 수 없으므로(NodeSharp.Nodes.Inject.csproj의 NR-03a 블록에 판단 근거
/// 기록), <see cref="InjectNode.TriggerAsync"/> 직접 호출을 "버튼 클릭"의 대역으로 삼아 실제
/// <see cref="FlowEngine"/> 배포·라우팅 경로로 이 완료 기준을 증명합니다(AskUserQuestion으로 확인한
/// 범위).
/// </summary>
public class InjectNodeTests
{
    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드 — FlowEngineRouteAsyncTests의 ReceiverNode와 동일한 패턴.</summary>
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 그대로 위임하는 테스트 전용 <see cref="INodeContext"/>(FlowEngineRouteAsyncTests.TestNodeContext와 동일한 패턴).</summary>
    private sealed class TestNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public TestNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
    }

    private static FlowEngine BuildEngine(out NodeTypeRegistry registry)
    {
        registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        // 완료 기준의 핵심 전제 — NodeSharp.Nodes.Inject 어셈블리(별도 프로젝트, Contracts만 참조)가
        // NodeTypeRegistry.ScanAssembly로 실제로 스캔·등록되는지부터 확인한다(InjectNodeType.Descriptor
        // 정적 필드 관례).
        registry.ScanAssembly(typeof(InjectNodeType).Assembly);
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public void InjectNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        BuildEngine(out var registry);

        Assert.True(registry.Descriptors.ContainsKey("inject"));
        Assert.Equal("input", registry.Descriptors["inject"].Category);
        Assert.Equal(0, registry.Descriptors["inject"].DefaultInputs);
        Assert.Equal(1, registry.Descriptors["inject"].DefaultOutputs);
        Assert.Single(registry.Descriptors["inject"].PropertySchema);
        Assert.Equal("payload", registry.Descriptors["inject"].PropertySchema[0].Key);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__TriggerAsync_1회_호출은_다음_노드에_정확히_1회_Msg를_전달한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine(out _);
        var injectCfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());
        var receiverCfg = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "Inject 테스트 플로우",
            Nodes: new[] { injectCfg, receiverCfg },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var injectNode = Assert.IsType<InjectNode>(engine.Nodes["n1"]);
        var ctx = new TestNodeContext(engine);
        await injectNode.TriggerAsync("수동 발행", ctx, CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("수동 발행", ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task TriggerAsync를_3회_호출하면_다음_노드가_정확히_3회_수신한다()
    {
        // "정확히 1회"가 우연이 아니라 호출 횟수와 정확히 비례한다는 것을 함께 확인 — TriggerAsync가
        // 내부적으로 여러 번 전달하거나 누락하지 않음을 보강 검증.
        ReceiverNode.Received.Clear();
        var engine = BuildEngine(out _);
        var injectCfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());
        var receiverCfg = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "Inject 반복 테스트",
            Nodes: new[] { injectCfg, receiverCfg },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var injectNode = Assert.IsType<InjectNode>(engine.Nodes["n1"]);
        var ctx = new TestNodeContext(engine);
        await injectNode.TriggerAsync(1, ctx, CancellationToken.None);
        await injectNode.TriggerAsync(2, ctx, CancellationToken.None);
        await injectNode.TriggerAsync(3, ctx, CancellationToken.None);

        Assert.Equal(3, ReceiverNode.Received.Count);
        Assert.Equal(new object?[] { 1, 2, 3 }, ReceiverNode.Received);
    }

    [Fact]
    public void InjectNode는_입력_포트가_0개이고_출력_포트가_1개다()
    {
        var node = new InjectNode { Id = "n1" };

        Assert.Empty(node.InputPorts);
        Assert.Single(node.OutputPorts);
    }
}
