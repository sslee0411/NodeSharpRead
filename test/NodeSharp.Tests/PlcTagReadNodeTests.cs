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
/// 완료 기준입니다).
/// (PD-01e, ★ 갱신) <see cref="PlcTagReadNode.OnInputAsync"/>가 <see cref="PlcTagReadNode.TagId"/>를
/// 그대로 전달하던 것에서 <c>ctx.GetTagValue(TagId)</c>로 읽은 실제(시뮬레이션) 값을 전달하도록
/// 바뀌면서, 아래 <see cref="NoopNodeContext"/>도 <see cref="INodeContext.GetTagValue"/>를 태그 Id별
/// 딕셔너리로 흉내 내도록 함께 바뀌었습니다 — "TagId는 무엇이 들어오든 흔들리지 않는다"는 원래
/// 증명 대상은 여전히 유효하되(같은 TagId면 같은 값을 조회), 이제는 그 TagId로 조회한 값 자체가
/// 다음 노드에 전달됨을 함께 확인합니다(값이 아직 갱신되지 않은 태그면 <c>null</c>이 그대로 전달되는
/// 것도 별도 테스트로 검증 — 클래스 문서 "null이면 그대로 null" 규약).
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

    /// <summary>
    /// 엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임만 하는, 상태 기록이 필요 없는 테스트 전용
    /// <see cref="INodeContext"/>(DebugNodeTests.RecordingNodeContext와 동일한 골격, Debug 호출은
    /// 기록하지 않음). (PD-01e, ★ 갱신) <paramref name="tagValues"/>(태그 Id → 값 딕셔너리, 생략하면
    /// 빈 딕셔너리)로 <see cref="GetTagValue"/>를 흉내 냅니다 — 실제 <c>NodeContext</c>(Runtime)의
    /// <c>TagValueCache</c> 조회를 대신하는 가장 단순한 형태입니다.
    /// </summary>
    private sealed class NoopNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        private readonly Dictionary<string, object?> _tagValues;
        public NoopNodeContext(FlowEngine engine, Dictionary<string, object?>? tagValues = null)
        {
            _engine = engine;
            _tagValues = tagValues ?? new Dictionary<string, object?>();
        }
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
        public void Debug(string nodeName, string msgJson) { }
        public object? GetTagValue(string tagId) => _tagValues.TryGetValue(tagId, out var value) ? value : null;
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
    public async Task OnInputAsync는_입력_payload와_무관하게_ctx_GetTagValue로_읽은_TagId_값을_다음_노드로_전달한다()
    {
        // (PD-01e, ★ 갱신) 완료 기준의 백엔드 토대: 지금 연동된 TagId로 ctx.GetTagValue를 조회한
        // "실제" 값이 무엇이든(입력 msg.Payload와 무관하게) 그 값 그대로 다음 노드에 도달해야 한다 —
        // Editor UI가 이름 변경 후에도 여전히 "같은 TagId"를 이 노드에 저장해두기만 하면 값 조회
        // 연동이 안 끊긴다는 것의 실행 시점 증명(예전엔 TagId 문자열 자체를 그대로 전달했었음).
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new PlcTagReadNode { Id = "tagread", Name = "태그읽기", TagId = "tag-guid-1" };
        var ctx = new NoopNodeContext(engine, new Dictionary<string, object?> { ["tag-guid-1"] = 42 });

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = "아무값" }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Equal(42, receiver.Received[0]);
    }

    [Fact]
    public async Task OnInputAsync는_아직_갱신된_적_없는_TagId면_null을_그대로_전달한다()
    {
        // (PD-01e, ★ 신규) PlcTagReadNode 클래스 문서의 "null이면 그대로 null" 규약 — 시뮬레이션
        // 모드가 아니거나 아직 첫 폴링 전인 태그는 오류가 아니라 null로 조용히 전달되어야 한다.
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new PlcTagReadNode { Id = "tagread", Name = "태그읽기", TagId = "tag-guid-1" };
        var ctx = new NoopNodeContext(engine); // 빈 tagValues — 아직 한 번도 갱신되지 않은 상태

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = "아무값" }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Null(receiver.Received[0]);
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
