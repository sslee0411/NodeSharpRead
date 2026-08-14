using Microsoft.CodeAnalysis.Scripting;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Function;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FunctionNode"/>/<see cref="FunctionNodeType"/>(FN-01·FN-02·FN-03·FN-04, 03번 개발 Step맵
/// Phase 7 — Function 노드 NCalc 표현식 실행기 + Roslyn C# 코드 실행기 + 모드별 필드 분리 + 실행
/// 타임아웃)에 대한 통합 테스트입니다. 완료 기준(03번 Step맵 FN-01): "수식 문법 오류(괄호 불일치 등)를
/// 입력해도 Runner가 크래시하지 않고 노드 에러로만 표면화되는지, 컴파일 없이 즉시 반영되는지 확인"과
/// 완료 기준(FN-02): "문법 오류는 컴파일 에러로 표면화되는지"를 <see cref="FlowEngine"/> 실제 배포·라우팅
/// 경로로 증명합니다(Inject/Switch 노드와 동일한 방식 — Editor→Runner IPC가 아직 없어 FunctionNode를
/// 직접 생성해 <see cref="FunctionNode.OnInputAsync"/>를 호출하는 것을 "메시지 도착"의 대역으로 삼음).
/// 완료 기준(FN-03): "ComboBox에서 NCalc↔Roslyn 전환 시 입력란이 즉시 전환되는지"는 WPF 다이얼로그가
/// 실제로 그려져야 확인 가능해 이 xUnit 프로젝트에서 자동 검증할 수 없고(EC-03 선례와 동일한 한계),
/// 대신 그 전환의 데이터 기반(<see cref="PropertyField.VisibleWhenKey"/> 배선과
/// <see cref="FunctionNode.ExpressionCode"/>/<see cref="FunctionNode.CSharpCode"/>가 모드별로 정확히
/// 독립 보존되는지)까지만 이 클래스에서 검증합니다. 완료 기준(FN-04): "무한루프 코드를 넣은 Function
/// 노드가 타임아웃 시간에 정확히 강제 중단되고 알림되는지"를 <see cref="OnInputAsync"/>를 거쳐
/// <c>ctx.SetStatus</c>(FN-01 결정 재사용)로 표면화되는지까지 이 클래스에서 검증하고, "강제 중단"의
/// 실제 의미(스레드 watchdog, 완전한 OS 강제 종료 아님)는 <see cref="RoslynFunctionExecutorTests"/>가
/// <c>RoslynFunctionExecutor.ExecuteAsync</c> 단위로 더 자세히 검증합니다.
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

        // (NR-11) INodeContext.Debug 신규 멤버 — 이 파일의 테스트 범위(Function 실행)와 무관해 무동작.
        public void Debug(string nodeName, string msgJson) { }
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
        // (FN-03) 단일 "code" 1개 → "mode"/"expressionCode"/"csharpCode" 3개로 분리(카드8 원본 설계).
        // (FN-04) "timeoutSec" 1개 추가로 총 4개.
        Assert.Equal(4, descriptor.PropertySchema.Count);
        Assert.Equal("mode", descriptor.PropertySchema[0].Key);
        Assert.Equal("expressionCode", descriptor.PropertySchema[1].Key);
        Assert.Equal("csharpCode", descriptor.PropertySchema[2].Key);
        Assert.Equal("timeoutSec", descriptor.PropertySchema[3].Key);
    }

    [Fact]
    public void FunctionNodeType_Descriptor는_expressionCode_csharpCode_timeoutSec에_mode_조건부_표시를_건다()
    {
        var descriptor = FunctionNodeType.Descriptor;

        var expressionField = descriptor.PropertySchema.Single(f => f.Key == "expressionCode");
        var csharpField = descriptor.PropertySchema.Single(f => f.Key == "csharpCode");
        var timeoutField = descriptor.PropertySchema.Single(f => f.Key == "timeoutSec");

        Assert.Equal("mode", expressionField.VisibleWhenKey);
        Assert.Equal("expression", expressionField.VisibleWhenValue);
        Assert.Equal("mode", csharpField.VisibleWhenKey);
        Assert.Equal("csharp", csharpField.VisibleWhenValue);
        // (FN-04) NCalc 모드는 반복문이 없어 타임아웃이 의미 없으므로 CSharp 모드에서만 노출.
        Assert.Equal("mode", timeoutField.VisibleWhenKey);
        Assert.Equal("csharp", timeoutField.VisibleWhenValue);
    }

    [Fact]
    public void Factory는_mode_csharp_문자열을_FunctionMode_CSharp로_매핑하고_csharpCode를_읽는다()
    {
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["csharpCode"] = "return msg;",
        });

        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(FunctionMode.CSharp, node.Mode);
        Assert.Equal("return msg;", node.CSharpCode);
    }

    [Fact]
    public void Factory는_mode가_없으면_기본값_Expression을_쓴다()
    {
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>());
        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(FunctionMode.Expression, node.Mode);
        Assert.Equal(string.Empty, node.ExpressionCode);
        Assert.Equal(string.Empty, node.CSharpCode);
    }

    [Fact]
    public void Factory는_expressionCode_csharpCode를_각각_독립적으로_보존한다()
    {
        // 완료 기준(FN-03): 모드를 오가도 두 필드는 서로 섞이지 않고 각자 저장된 값을 유지해야 한다.
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["expressionCode"] = "payload * 2",
            ["csharpCode"] = "return msg;",
        });

        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(FunctionMode.CSharp, node.Mode);
        Assert.Equal("payload * 2", node.ExpressionCode);
        Assert.Equal("return msg;", node.CSharpCode);
    }

    [Fact]
    public void Factory는_구버전_단일_code_키를_저장_당시_mode에_맞는_새_필드로_옮겨_읽는다()
    {
        // (FN-03) FN-01/FN-02 시절 저장된 flows.json 호환 — 새 키(expressionCode/csharpCode)가 없고
        // 옛 "code" 키만 있으면, 함께 저장된 "mode"에 맞는 새 필드로 옮겨 읽어야 값이 사라지지 않는다.
        var expressionCfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "expression",
            ["code"] = "payload * 2",
        });
        var csharpCfg = new NodeConfig("n2", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["code"] = "return msg;",
        });

        var expressionNode = (FunctionNode)FunctionNodeType.Descriptor.Factory(expressionCfg);
        var csharpNode = (FunctionNode)FunctionNodeType.Descriptor.Factory(csharpCfg);

        Assert.Equal("payload * 2", expressionNode.ExpressionCode);
        Assert.Equal(string.Empty, expressionNode.CSharpCode);
        Assert.Equal("return msg;", csharpNode.CSharpCode);
        Assert.Equal(string.Empty, csharpNode.ExpressionCode);
    }

    [Fact]
    public void Factory는_timeoutSec가_없으면_기본값_5초를_쓴다()
    {
        // (FN-04) "timeoutSec" 키 자체가 없는 구버전(FN-01~FN-03 시절) flows.json도 기본 5초로 정상 동작해야 함.
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["csharpCode"] = "return msg;",
        });

        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(5.0, node.TimeoutSeconds);
    }

    [Fact]
    public void Factory는_timeoutSec를_지정하면_그대로_읽는다()
    {
        var cfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["csharpCode"] = "return msg;",
            ["timeoutSec"] = "2.5",
        });

        var node = (FunctionNode)FunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal(2.5, node.TimeoutSeconds);
    }

    [Fact]
    public void Factory는_timeoutSec가_0_이하_또는_숫자가_아니면_기본값_5초로_대체한다()
    {
        // (FN-04) 잘못된 값(음수·0·문자열 등)으로 타임아웃이 사실상 무력화되는 것을 막는 방어 로직.
        var zeroCfg = new NodeConfig("n1", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp", ["csharpCode"] = "return msg;", ["timeoutSec"] = "0",
        });
        var negativeCfg = new NodeConfig("n2", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp", ["csharpCode"] = "return msg;", ["timeoutSec"] = "-3",
        });
        var invalidCfg = new NodeConfig("n3", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp", ["csharpCode"] = "return msg;", ["timeoutSec"] = "not-a-number",
        });

        Assert.Equal(5.0, ((FunctionNode)FunctionNodeType.Descriptor.Factory(zeroCfg)).TimeoutSeconds);
        Assert.Equal(5.0, ((FunctionNode)FunctionNodeType.Descriptor.Factory(negativeCfg)).TimeoutSeconds);
        Assert.Equal(5.0, ((FunctionNode)FunctionNodeType.Descriptor.Factory(invalidCfg)).TimeoutSeconds);
    }

    [Fact]
    public async Task OnInputAsync는_정상_표현식이면_결과를_다음_노드로_전달한다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.Expression, ExpressionCode = "payload * 2" };
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
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.Expression, ExpressionCode = "(1 + 2" }; // 괄호 불일치
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
    public async Task OnStartAsync는_CSharp_모드_정상_코드면_예외_없이_컴파일된다()
    {
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.CSharp, CSharpCode = "return msg;" };
        var ctx = new RecordingNodeContext(new FlowEngine(new NodeTypeRegistry(contractsVersion: "1.0.0")));

        var exception = await Record.ExceptionAsync(() => node.OnStartAsync(ctx, CancellationToken.None));

        Assert.Null(exception);
        Assert.IsType<RoslynFunctionExecutor>(node.Executor);
    }

    [Fact]
    public async Task OnStartAsync는_CSharp_모드_문법_오류_코드면_CompilationErrorException을_던진다()
    {
        var node = new FunctionNode { Id = "fn", Mode = FunctionMode.CSharp, CSharpCode = "return msg + ;" }; // 문법 오류
        var ctx = new RecordingNodeContext(new FlowEngine(new NodeTypeRegistry(contractsVersion: "1.0.0")));

        await Assert.ThrowsAsync<CompilationErrorException>(() => node.OnStartAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task OnInputAsync는_CSharp_모드_정상_코드면_결과를_다음_노드로_전달한다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new FunctionNode
        {
            Id = "fn",
            Mode = FunctionMode.CSharp,
            CSharpCode = "msg.payload = (double)msg.payload * 2; return msg;",
        };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 21.0 }, ctx, CancellationToken.None);

        Assert.Single(receiver.Received);
        Assert.Equal(42.0, Convert.ToDouble(receiver.Received[0]));
        Assert.Empty(ctx.StatusCalls);
    }

    [Fact]
    public async Task OnInputAsync는_CSharp_모드_무한루프_코드면_타임아웃_시간_안에_SetStatus로만_표면화하고_크래시하지_않는다()
    {
        // 완료 기준(FN-04): "무한루프 코드를 넣은 Function 노드가 타임아웃 시간에 정확히 강제 중단되고
        // 알림되는지 확인" — TimeoutSeconds를 0.1초로 짧게 잡아 while(true){}를 넣어도 이 메서드
        // 호출 자체는 빠르게 반환되고(FN-01과 동일하게 예외를 밖으로 던지지 않음), SetStatus(빨강)로만
        // 표면화되는지 검증한다. "진짜 강제 종료"가 아닌 watchdog 방식의 한계는
        // RoslynFunctionExecutorTests·FunctionTimeoutException 코드 문서에 근거를 남김.
        var (engine, receiver) = BuildWireOnlyEngine();
        var node = new FunctionNode
        {
            Id = "fn",
            Mode = FunctionMode.CSharp,
            CSharpCode = "while (true) { }",
            TimeoutSeconds = 0.1,
        };
        var ctx = new RecordingNodeContext(engine);

        await node.OnStartAsync(ctx, CancellationToken.None);
        var exception = await Record.ExceptionAsync(() =>
            node.OnInputAsync(new Msg(), ctx, CancellationToken.None));

        Assert.Null(exception);           // Runner를 죽이지 않고 여기서 흡수돼야 함(RT-04a 경계 유지, FN-01과 동일)
        Assert.Empty(receiver.Received);  // 다음 노드로는 전달되지 않음
        Assert.Single(ctx.StatusCalls);
        Assert.Equal("red", ctx.StatusCalls[0].Fill);
        Assert.Contains("타임아웃", ctx.StatusCalls[0].Text);
    }

    [Fact]
    public void DeployAsync는_CSharp_모드_문법_오류_Function_노드를_FailedNodeIds에_기록하고_다른_노드는_계속_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(FunctionNodeType).Assembly);
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var fnConfig = new NodeConfig("fn", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["csharpCode"] = "return msg + ;", // 문법 오류
        });
        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { fnConfig, receiverConfig }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains("fn", engine.FailedNodeIds);
        Assert.DoesNotContain("r0", engine.FailedNodeIds);
    }

    [Fact]
    public void DeployAsync는_CSharp_모드_정상_코드_Function_노드는_FailedNodeIds에_기록되지_않는다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(FunctionNodeType).Assembly);

        var fnConfig = new NodeConfig("fn", "function", "테스트", "f1", new Dictionary<string, object?>
        {
            ["mode"] = "csharp",
            ["csharpCode"] = "return msg;",
        });

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { fnConfig }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.DoesNotContain("fn", engine.FailedNodeIds);
    }
}
