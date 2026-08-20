using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.PlcTagRead;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="PlcTagReadNode"/>/<see cref="PlcTagReadNodeType"/>(ED-D04, 03번 개발 Step맵 Phase 9 —
/// TagRef 연동)에 대한 단위/통합 테스트입니다. 이 클래스는 백엔드(Descriptor 등록·Factory·FlowEngine
/// 배포·라우팅 경로)만 검증합니다 — 완료 기준의 "구조 설정에서 태그 이름만 변경해도 캔버스 노드의
/// TagRef 연동이 끊기지 않는지"는 실제 WPF NodePropertyDialog(TagRef 콤보박스)·StructureView
/// (TagCatalog 갱신)의 런타임 동작을 요구해 이 xUnit 프로젝트로는 검증할 수 없습니다(ED-D01/
/// ED-D02a/ED-D03과 동일한 선례 — 03번 Step맵 ED-D04 항목 참고, 사용자의 Windows 실행 확인이 최종
/// 완료 기준입니다). 여기서는 대신 "TagId 값 자체는 무엇이 들어오든 그대로 안정적으로 전달된다"는
/// 것을 증명해, Editor UI가 그 위에 얹는 연동이 깨지지 않을 백엔드 토대를 확인합니다.
/// </summary>
public class PlcTagReadNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드(DebugNodeTests/FunctionNodeTests와 동일한 패턴).</summary>
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임만 하는, 상태 기록이 필요 없는 테스트 전용 <see cref="INodeContext"/>(DebugNodeTests.RecordingNodeContext와 동일한 골격, Debug 호출은 기록하지 않음).</summary>
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

    /// <summary>수신 노드 r0 하나를 "tagread"의 0번 출력 포트에 와이어로 연결해 배포한다(DebugNodeTests.BuildWireOnlyEngine과 동일한 패턴).</summary>
    private static (FlowEngine Engine, ReceiverNode Receiver) BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());
        var wires = new List<Wire> { new Wire("tagread", 0, "r0", 0) };

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { receiverConfig }, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        return (engine, (ReceiverNode)engine.Nodes["r0"]);
    }

    [Fact]
    public void PlcTagReadNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(PlcTagReadNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("plcTagRead"));
        var descriptor = registry.Descriptors["plcTagRead"];
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Single(descriptor.PropertySchema);
        Assert.Equal("tagId", descriptor.PropertySchema[0].Key);
        Assert.Equal(PropertyFieldType.TagRef, descriptor.PropertySchema[0].Type);
        Assert.True(descriptor.PropertySchema[0].Required);
    }

    [Fact]
    public void Factory는_tagId를_그대로_읽는다()
    {
        var cfg = new NodeConfig("n1", "plcTagRead", "테스트", "f1", new Dictionary<string, object?>
        {
            ["tagId"] = "abc-123",
        });
        var node = (PlcTagReadNode)PlcTagReadNodeType.Descriptor.Factory(cfg);

        Assert.Equal("abc-123", node.TagId);
    }

    [Fact]
    public void Factory는_tagId가_없으면_빈문자열을_쓴다()
    {
        var cfg = new NodeConfig("n1", "plcTagRead", "테스트", "f1", new Dictionary<string, object?>());
        var node = (PlcTagReadNode)PlcTagReadNodeType.Descriptor.Factory(cfg);

        Assert.Equal(string.Empty, node.TagId);
    }

    [Fact]
    public async Task OnInputAsync는_입력_payload와_무관하게_항상_TagId를_다음_노드로_전달한다()
    {
        // 완료 기준의 백엔드 토대: 실제 PLC 값(아직 미구현)이 무엇이든, 지금 연동된 TagId 자체는
        // 흔들리지 않고 항상 그대로 다음 노드에 도달해야 한다 — Editor UI가 이름 변경 후에도 여전히
        // "같은 TagId"를 이 노드에 저장해두기만 하면 연동이 안 끊긴다는 것의 실행 시점 증명.
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new PlcTagReadNode { Id = "tagread", Name = "태그읽기", TagId = "tag-guid-1" };
        var ctx = new NoopNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = "아무값" }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Equal("tag-guid-1", receiver.Received[0]);
    }

    [Fact]
    public void DeployAsync는_PlcTagRead_노드를_다른_노드와_동일하게_정상_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(PlcTagReadNodeType).Assembly);

        var cfg = new NodeConfig("tagread", "plcTagRead", "태그읽기", "f1", new Dictionary<string, object?>
        {
            ["tagId"] = "tag-guid-1",
        });

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { cfg }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.DoesNotContain("tagread", engine.FailedNodeIds);
    }
}
