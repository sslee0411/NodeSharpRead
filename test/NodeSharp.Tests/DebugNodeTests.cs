using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Debug;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="DebugNode"/>/<see cref="DebugNodeType"/>(NR-11, 03번 개발 Step맵 Phase 7 — Debug 노드,
/// Inject→Function→Switch→Debug 4개 코어 노드 중 마지막)에 대한 통합 테스트입니다. 완료 기준(03번
/// Step맵 NR-11) 3개 중 이 클래스가 다루는 범위: ①"Debug 노드에 임의 Msg를 흘려보내면
/// DebugMessageEvent가 발행되는지"는 실제 <c>DebugMessageEvent</c> 발행 대신
/// <see cref="INodeContext.Debug"/> 호출 여부·인자로 검증합니다(이벤트 발행 자체는
/// <c>NodeContext.Debug</c>(Runtime) 몫이고, <c>NodeContext</c>가 <c>IEventBus</c>로 정확히 그 이벤트를
/// 감싸 발행한다는 것은 <c>SetStatus</c>/<c>NodeStatusEvent</c> 선례와 동일한 위임 구조라 이미
/// 신뢰할 수 있음). ②"다음 노드로 전달 On/Off에 따라 다운스트림 전달 여부가 달라지는지"는
/// <see cref="FlowEngine"/> 실제 배포·라우팅 경로로 직접 증명합니다. ③"Pause 상태에서는 사이드바가
/// 갱신되지 않다가 다시 표시되는지"는 그 대상인 Editor 디버그 사이드바가 아직 없어(LK-02 계열
/// ⏳ 대기, DebugNode.cs XML 문서 참고) 이 xUnit 프로젝트에서 검증할 수 없습니다.
/// </summary>
public class DebugNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드(FunctionNodeTests와 동일한 패턴).</summary>
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임하고 <see cref="INodeContext.Debug"/> 호출을 기록하는 테스트 전용 <see cref="INodeContext"/>.</summary>
    private sealed class RecordingNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public RecordingNodeContext(FlowEngine engine) => _engine = engine;
        public List<(string NodeName, string MsgJson)> DebugCalls { get; } = new();
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
        public void Debug(string nodeName, string msgJson) => DebugCalls.Add((nodeName, msgJson));
    }

    /// <summary>수신 노드 r0 하나를 "dbg"의 0번 출력 포트에 와이어로 연결해 배포한다 — "dbg" 자체는 NodeConfig 없이 두어(FunctionNodeTests와 동일 패턴) 테스트가 DebugNode를 직접 생성할 수 있게 한다.</summary>
    private static (FlowEngine Engine, ReceiverNode Receiver) BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());
        var wires = new List<Wire> { new Wire("dbg", 0, "r0", 0) };

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { receiverConfig }, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        return (engine, (ReceiverNode)engine.Nodes["r0"]);
    }

    [Fact]
    public void DebugNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(DebugNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("debug"));
        var descriptor = registry.Descriptors["debug"];
        Assert.Equal("common", descriptor.Category); // 실제 Node-RED 원본과 동일(WebSearch로 확인, DebugNodeType.cs XML 문서 참고)
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Single(descriptor.PropertySchema);
        Assert.Equal("toNext", descriptor.PropertySchema[0].Key);
    }

    [Fact]
    public void Factory는_toNext가_없으면_기본값_false를_쓴다()
    {
        var cfg = new NodeConfig("n1", "debug", "테스트", "f1", new Dictionary<string, object?>());
        var node = (DebugNode)DebugNodeType.Descriptor.Factory(cfg);

        Assert.False(node.ToNext);
    }

    [Fact]
    public void Factory는_toNext_true_문자열을_true로_읽는다()
    {
        var cfg = new NodeConfig("n1", "debug", "테스트", "f1", new Dictionary<string, object?>
        {
            ["toNext"] = "true",
        });
        var node = (DebugNode)DebugNodeType.Descriptor.Factory(cfg);

        Assert.True(node.ToNext);
    }

    [Fact]
    public async Task OnInputAsync는_ToNext_값과_무관하게_항상_ctx_Debug를_호출한다()
    {
        // 완료 기준 ①: 임의 Msg를 흘려보내면 항상 발행(표시)돼야 한다 — "다음 노드로 전달"을 끄더라도
        // 사이드바 표시(발행) 자체는 꺼지면 안 된다(DebugNode.cs XML 문서 근거).
        var (engine, _) = BuildWireOnlyEngine();
        var node = new DebugNode { Id = "dbg", Name = "디버그", ToNext = false };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 42 }, ctx, CancellationToken.None);

        Assert.Single(ctx.DebugCalls);
        Assert.Equal("디버그", ctx.DebugCalls[0].NodeName);
        Assert.Contains("42", ctx.DebugCalls[0].MsgJson); // Msg.ToJson() 결과에 payload 값이 포함돼야 함
    }

    [Fact]
    public async Task OnInputAsync는_ToNext가_false면_발행만_하고_다음_노드로_전달하지_않는다()
    {
        // 완료 기준 ②(끔): "다음 노드로 전달"이 꺼져 있으면 다운스트림에 도달하지 않아야 한다.
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new DebugNode { Id = "dbg", Name = "디버그", ToNext = false };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 42 }, ctx, CancellationToken.None);

        Assert.Single(ctx.DebugCalls);   // 발행은 여전히 됨
        Assert.Empty(receiver.Received); // 하지만 다음 노드로는 전달 안 됨
    }

    [Fact]
    public async Task OnInputAsync는_ToNext가_true면_발행하고_다음_노드로도_전달한다()
    {
        // 완료 기준 ②(켬): "다음 노드로 전달"이 켜져 있으면 다운스트림에도 도달해야 한다.
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new DebugNode { Id = "dbg", Name = "디버그", ToNext = true };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 42 }, ctx, CancellationToken.None);

        Assert.Single(ctx.DebugCalls);
        Assert.Single(receiver.Received);
        Assert.Equal(42, Convert.ToInt32(receiver.Received[0]));
    }

    [Fact]
    public void DeployAsync는_Debug_노드를_다른_노드와_동일하게_정상_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(DebugNodeType).Assembly);

        var dbgConfig = new NodeConfig("dbg", "debug", "디버그", "f1", new Dictionary<string, object?>
        {
            ["toNext"] = "false",
        });

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { dbgConfig }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.DoesNotContain("dbg", engine.FailedNodeIds);
    }
}
