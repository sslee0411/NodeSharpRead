using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.SequenceTrigger;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SequenceTriggerNode"/>/<see cref="SequenceTriggerNodeType"/>(SQ-03, 03번 개발 Step맵
/// Phase 10 — 편도/역방향 내비게이션)에 대한 단위/통합 테스트입니다. 이 클래스는 백엔드(Descriptor
/// 등록·Factory·FlowEngine 배포/라우팅 경로)만 검증합니다 — 완료 기준의 "더블클릭 시 시퀀스 창이
/// 열리고, '호출하는 Flow 노드 보기' 실행 시 캔버스 노드가 하이라이트되는지"는 실제 WPF
/// FlowCanvasView/SequenceEditorWindow의 런타임 동작을 요구해 이 xUnit 프로젝트로는 검증할 수 없습니다
/// (PlcTagReadNodeTests/ED-D04와 동일한 선례 — 사용자의 Windows 실행 확인이 최종 완료 기준입니다).
/// </summary>
public class SequenceTriggerNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드(PlcTagReadNodeTests.ReceiverNode와 동일한 패턴).</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public List<object?> Received { get; } = new();

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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임만 하는 최소 <see cref="INodeContext"/>(PlcTagReadNodeTests.NoopNodeContext와 동일한 골격, GetTagValue는 이 노드가 아직 쓰지 않아 생략).</summary>
    private sealed class NoopNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public NoopNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
        public void Debug(string nodeName, string msgJson) { }
    }

    /// <summary>수신 노드 r0 하나를 "trigger"의 0번 출력 포트에 와이어로 연결해 배포한다(PlcTagReadNodeTests.BuildWireOnlyEngine과 동일한 패턴).</summary>
    private static (FlowEngine Engine, ReceiverNode Receiver) BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());
        var wires = new List<Wire> { new Wire("trigger", 0, "r0", 0) };

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { receiverConfig }, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        return (engine, (ReceiverNode)engine.Nodes["r0"]);
    }

    [Fact]
    public void SequenceTriggerNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(SequenceTriggerNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("sequenceTrigger"));
        var descriptor = registry.Descriptors["sequenceTrigger"];
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Single(descriptor.PropertySchema);
        Assert.Equal("sequenceId", descriptor.PropertySchema[0].Key);
        Assert.Equal(PropertyFieldType.SequenceRef, descriptor.PropertySchema[0].Type);
        Assert.True(descriptor.PropertySchema[0].Required);
    }

    [Fact]
    public void Factory는_sequenceId를_그대로_읽는다()
    {
        var cfg = new NodeConfig("n1", "sequenceTrigger", "테스트", "f1", new Dictionary<string, object?>
        {
            ["sequenceId"] = "seq-1",
        });
        var node = (SequenceTriggerNode)SequenceTriggerNodeType.Descriptor.Factory(cfg);

        Assert.Equal("seq-1", node.SequenceId);
    }

    [Fact]
    public void Factory는_sequenceId가_없으면_빈문자열을_쓴다()
    {
        var cfg = new NodeConfig("n1", "sequenceTrigger", "테스트", "f1", new Dictionary<string, object?>());
        var node = (SequenceTriggerNode)SequenceTriggerNodeType.Descriptor.Factory(cfg);

        Assert.Equal(string.Empty, node.SequenceId);
    }

    [Fact]
    public async Task OnInputAsync는_아직_패스스루이므로_입력_payload를_그대로_다음_노드로_전달한다()
    {
        // (SQ-03, ★ 범위 축소) 실제 SequenceExecutor 시작은 후속 Step 범위 — 지금은 msg를 그대로
        // 전달하는지만 증명한다(클래스 XML 문서 "범위 축소" 참고).
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new SequenceTriggerNode { Id = "trigger", Name = "펌프 기동 절차", SequenceId = "seq-1" };
        var ctx = new NoopNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = "아무값" }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Equal("아무값", receiver.Received[0]);
    }

    [Fact]
    public void DeployAsync는_SequenceTrigger_노드를_다른_노드와_동일하게_정상_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(SequenceTriggerNodeType).Assembly);

        var cfg = new NodeConfig("trigger", "sequenceTrigger", "펌프 기동 절차", "f1", new Dictionary<string, object?>
        {
            ["sequenceId"] = "seq-1",
        });

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { cfg }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.DoesNotContain("trigger", engine.FailedNodeIds);
    }
}
