using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Function;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FunctionNode"/>/<see cref="FunctionNodeType"/>(FN-01, 03번 개발 Step맵 Phase 7 —
/// Function 노드 NCalc 표현식 실행기)에 대한 통합 테스트입니다. 완료 기준(03번 Step맵 FN-01): "수식
/// 문법 오류(괄호 불일치 등)를 입력해도 Runner가 크래시하지 않고 노드 에러로만 표면화되는지, 컴파일
/// 없이 즉시 반영되는지 확인"을 <see cref="FlowEngine"/> 실제 배포·라우팅 경로로 증명합니다(Inject/
/// Switch 노드와 동일한 방식 — Editor→Runner IPC가 아직 없어 FunctionNode를 직접 생성해
/// <see cref="FunctionNode.OnInputAsync"/>를 호출하는 것을 "메시지 도착"의 대역으로 삼음).
/// </summary>
public class FunctionNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드(SwitchNodeTests와 동일한 패턴).</summary>
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임하고 <see cref="SetStatus"/> 호출을 기록하는 테스트 전용 <see cref="INodeContext"/> — FN-01 예외 격리(빨간 상태 점 표시) 검증용.</summary>
    private sealed class RecordingNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public RecordingNodeContext(FlowEngine engine) => _engine = engine;
        public List<(string Fill, string Shape, string Text)> StatusCalls { get; } = new();
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) => StatusCalls.Add((fill, shape, text));
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
    }

    /// <summary>수신 노드 r0 하나를 "fn"의 0번 출력 포트에 와이어로 연결해 배포한다 — "fn" 자체는 NodeConfig 없이 두어(SwitchNodeTests와 동일 패턴) 테스트가 FunctionNode를 직접 생성할 수 있게 한다.</summary>
    private static (FlowEngine Engine, ReceiverNode Receiver) BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());
        var wires = new List<Wire> { new Wire("fn", 0, "r0", 0) };

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { receiverConfig }, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        return (engine, (ReceiverNode)engine.Nodes["r0"]);
    }

    [Fact]
    public void FunctionNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(FunctionNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("function"));
        var descriptor = registry.Descriptors["function"];
        Assert.Equal("function", descriptor.Category);
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Equal(2, descriptor.PropertySchema.Count);
        Assert.Equal("mode", descriptor.PropertySchema[0].Key);
        Assert.Equal("code", descriptor.PropertySchema[1].Key);
    }

    [Fact]
    public void Factory는_mode_csharp_문자열을_FunctionMode_CSharp로_매핑한다()
    {
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["code"] = "return msg;",
        });

        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(FunctionMode.CSharp, node.Mode);
        Assert.Equal("return msg;", node.Code);
    }

    [Fact]
    public void Factory는_mode가_없으면_기본값_Expression을_쓴다()
    {
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>());
        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(FunctionMode.Expression, node.Mode);
        Assert.Equal(string.Empty, node.Code);
    }

    [Fact]
    public async Task OnInputAsync는_정상_표현식이면_결과를_다음_노드로_전달한다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.Expression, Code = "payload * 2" };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 21 }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Equal(42, Convert.ToInt32(receiver.Received[0]));
        Assert.Empty(ctx.StatusCalls);
    }

    [Fact]
    public async Task OnInputAsync는_문법_오류_표현식이면_크래시하지_않고_SetStatus로만_표면화한다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.Expression, Code = "(1 + 2" }; // 괄호 불일치
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        var exception = await Record.ExceptionAsync(() =>
            node.OnInputAsync(new Msg { Payload = 1 }, ctx, CancellationToken.None));

        Assert.Null(exception);           // Runner를 죽이지 않고 여기서 흡수돼야 함(RT-04a 경계 유지)
        Assert.Empty(receiver.Received);  // 다음 노드로는 전달되지 않음
        Assert.Single(ctx.StatusCalls);
        Assert.Equal("red", ctx.StatusCalls[0].Fill);
    }

    [Fact]
    public async Task OnStartAsync는_CSharp_모드면_NotSupportedException을_던진다()
    {
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.CSharp, Code = "return msg;" };
        var ctx = new RecordingNodeContext(new FlowEngine(new NodeTypeRegistry(contractsVersion: "1.0.0")));

        await Assert.ThrowsAsync<NotSupportedException>(() => node.OnStartAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public void DeployAsync는_CSharp_모드_Function_노드를_FailedNodeIds에_기록하고_다른_노드는_계속_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(FunctionNodeType).Assembly);
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var fnConfig = new NodeConfig("fn", "function", "테스트", "f1", new Dictionary<string, object?> { ["mode"] = "csharp" });
        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { fnConfig, receiverConfig }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains("fn", engine.FailedNodeIds);
        Assert.DoesNotContain("r0", engine.FailedNodeIds);
    }
}
