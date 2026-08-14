using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Switch;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SwitchNode"/>/<see cref="SwitchNodeType"/>(NR-04, 03번 개발 Step맵 Phase 7 — Switch
/// 노드의 첫 구현체)에 대한 통합 테스트입니다. 완료 기준(03번 Step맵 NR-04): "조건 3개 이상을 설정했을
/// 때 값에 맞는 포트로만 라우팅되고 맞지 않는 값은 어느 포트로도 나가지 않는지, 비교값을 TypedValue의
/// MsgField/FlowContext Source로 설정해도 정상 비교되는지 확인"을 <see cref="FlowEngine"/> 실제 배포·
/// 라우팅 경로로 증명합니다(InjectNodeTests와 동일한 방식 — Editor→Runner IPC가 아직 없어 SwitchNode를
/// 직접 생성해 <see cref="SwitchNode.OnInputAsync"/>를 호출하는 것을 "메시지 도착"의 대역으로 삼음).
/// </summary>
public class SwitchNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드.</summary>
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임하고, Flow/Global Context도 실제로 동작하는 테스트 전용 <see cref="INodeContext"/>.</summary>
    private sealed class TestNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public TestNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);

        // (NR-11) INodeContext.Debug 신규 멤버 — 이 파일의 테스트 범위(Switch 라우팅)와 무관해 무동작.
        public void Debug(string nodeName, string msgJson) { }
    }

    /// <summary>수신 노드 <paramref name="portCount"/>개(r0, r1, ...)를 각각 "sw"의 0..N-1번 출력 포트에 와이어로 연결해 배포한다 — "sw" 자체는 NodeConfig 없이 두어(BuildWireOnlyEngine, InjectNodeTests와 동일 패턴) 테스트가 SwitchNode를 직접 생성할 수 있게 한다.</summary>
    private static (FlowEngine Engine, ReceiverNode[] Receivers) BuildWireOnlyEngine(int portCount)
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfigs = new List<NodeConfig>();
        var wires = new List<Wire>();
        for (var i = 0; i < portCount; i++)
        {
            var id = $"r{i}";
            receiverConfigs.Add(new NodeConfig(id, "receiver", id, "f1", new Dictionary<string, object?>()));
            wires.Add(new Wire("sw", i, id, 0));
        }

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우", Nodes: receiverConfigs, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        var receivers = Enumerable.Range(0, portCount).Select(i => (ReceiverNode)engine.Nodes[$"r{i}"]).ToArray();
        return (engine, receivers);
    }

    [Fact]
    public void SwitchNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(SwitchNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("switch"));
        var descriptor = registry.Descriptors["switch"];
        Assert.Equal("function", descriptor.Category);
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Equal(3, descriptor.PropertySchema.Count);
        Assert.Equal("property", descriptor.PropertySchema[0].Key);
        Assert.Equal("rules", descriptor.PropertySchema[1].Key);
        Assert.Equal("checkall", descriptor.PropertySchema[2].Key);
    }

    [Fact]
    public void Factory는_rules_JSON을_SwitchRule_목록으로_역직렬화한다()
    {
        var cfg = new NodeConfig("n1", "switch", "테스트", "f1", new Dictionary<string, object?>
        {
            ["rules"] = "[{\"Operator\":\"gte\",\"CompareValue\":{\"Source\":0,\"Value\":\"85\"}},{\"Operator\":\"else\"}]",
            ["checkall"] = "false",
        });

        var node = (SwitchNode)SwitchNodeType.Descriptor.Factory(cfg);

        Assert.Equal(2, node.Rules.Count);
        Assert.Equal("gte", node.Rules[0].Operator);
        Assert.Equal(TypedValueSource.Fixed, node.Rules[0].CompareValue!.Source);
        Assert.Equal("85", node.Rules[0].CompareValue!.Value);
        Assert.Equal("else", node.Rules[1].Operator);
        Assert.False(node.CheckAll);
        Assert.Equal(2, node.OutputPorts.Count);
    }

    [Fact]
    public void Factory는_rules가_없으면_빈_목록과_기본_포트_1개를_만든다()
    {
        var cfg = new NodeConfig("n1", "switch", "테스트", "f1", new Dictionary<string, object?>());
        var node = (SwitchNode)SwitchNodeType.Descriptor.Factory(cfg);

        Assert.Empty(node.Rules);
        Assert.Single(node.OutputPorts);
        Assert.True(node.CheckAll); // 기본값 true
        Assert.Equal(TypedValueSource.MsgField, node.Property.Source);
        Assert.Equal("payload", node.Property.Value);
    }

    [Fact]
    public async Task 규칙_3개_이상이면_값에_맞는_포트로만_라우팅되고_나머지는_비어있다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 3);
        var ctx = new TestNodeContext(engine);
        var node = new SwitchNode
        {
            Id = "sw",
            Rules = new[]
            {
                new SwitchRule("lt", CompareValue: new TypedValue(TypedValueSource.Fixed, "0")),
                new SwitchRule("btwn",
                    CompareValue: new TypedValue(TypedValueSource.Fixed, "0"),
                    CompareValue2: new TypedValue(TypedValueSource.Fixed, "100")),
                new SwitchRule("gt", CompareValue: new TypedValue(TypedValueSource.Fixed, "100")),
            },
        };

        await node.OnInputAsync(new Msg { Payload = 50 }, ctx, CancellationToken.None);

        Assert.Empty(receivers[0].Received);      // lt 0 — 안 맞음
        Assert.Single(receivers[1].Received);      // btwn 0..100 — 맞음
        Assert.Empty(receivers[2].Received);       // gt 100 — 안 맞음
        Assert.Equal(50, receivers[1].Received[0]);
    }

    [Fact]
    public async Task 어느_규칙에도_안_맞으면_모든_포트가_비어있다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 2);
        var ctx = new TestNodeContext(engine);
        var node = new SwitchNode
        {
            Id = "sw",
            Rules = new[]
            {
                new SwitchRule("eq", CompareValue: new TypedValue(TypedValueSource.Fixed, "1")),
                new SwitchRule("eq", CompareValue: new TypedValue(TypedValueSource.Fixed, "2")),
            },
        };

        await node.OnInputAsync(new Msg { Payload = 99 }, ctx, CancellationToken.None);

        Assert.Empty(receivers[0].Received);
        Assert.Empty(receivers[1].Received);
    }

    [Fact]
    public async Task CheckAll이_false면_첫_매치_포트에서만_멈춘다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 2);
        var ctx = new TestNodeContext(engine);
        var node = new SwitchNode
        {
            Id = "sw",
            CheckAll = false,
            Rules = new[]
            {
                new SwitchRule("gte", CompareValue: new TypedValue(TypedValueSource.Fixed, "0")),
                new SwitchRule("gte", CompareValue: new TypedValue(TypedValueSource.Fixed, "-10")),
            },
        };

        // 두 규칙 모두 5에 대해 참이지만, CheckAll=false라 0번 포트에서 멈춰야 한다.
        await node.OnInputAsync(new Msg { Payload = 5 }, ctx, CancellationToken.None);

        Assert.Single(receivers[0].Received);
        Assert.Empty(receivers[1].Received);
    }

    [Fact]
    public async Task Else_규칙은_다른_규칙이_아무것도_안_맞았을_때만_매치한다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 2);
        var ctx = new TestNodeContext(engine);
        var node = new SwitchNode
        {
            Id = "sw",
            Rules = new[]
            {
                new SwitchRule("eq", CompareValue: new TypedValue(TypedValueSource.Fixed, "1")),
                new SwitchRule("else"),
            },
        };

        await node.OnInputAsync(new Msg { Payload = 1 }, ctx, CancellationToken.None);
        Assert.Single(receivers[0].Received);
        Assert.Empty(receivers[1].Received);

        await node.OnInputAsync(new Msg { Payload = 2 }, ctx, CancellationToken.None);
        Assert.Single(receivers[0].Received); // 여전히 1개(이번엔 안 늘어남)
        Assert.Single(receivers[1].Received);  // else가 매치
    }

    [Fact]
    public async Task 비교값을_FlowContext_Source로_지정해도_정상_비교된다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 1);
        var ctx = new TestNodeContext(engine);
        ctx.Flow.Set("threshold", 80.0);

        var node = new SwitchNode
        {
            Id = "sw",
            Rules = new[]
            {
                new SwitchRule("gte", CompareValue: new TypedValue(TypedValueSource.FlowContext, "threshold")),
            },
        };

        await node.OnInputAsync(new Msg { Payload = 90 }, ctx, CancellationToken.None);
        Assert.Single(receivers[0].Received);

        await node.OnInputAsync(new Msg { Payload = 70 }, ctx, CancellationToken.None);
        Assert.Single(receivers[0].Received); // 70 < 80이라 안 늘어남
    }

    [Fact]
    public async Task Property를_MsgField_이외로_지정하면_그_출처의_값으로_비교한다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 1);
        var ctx = new TestNodeContext(engine);
        ctx.Global.Set("mode", "auto");

        var node = new SwitchNode
        {
            Id = "sw",
            Property = new TypedValue(TypedValueSource.GlobalContext, "mode"),
            Rules = new[]
            {
                new SwitchRule("eq", CompareValue: new TypedValue(TypedValueSource.Fixed, "auto")),
            },
        };

        await node.OnInputAsync(new Msg { Payload = "무관한 값" }, ctx, CancellationToken.None);
        Assert.Single(receivers[0].Received);
    }

    [Fact]
    public async Task 규칙이_없으면_아무_포트로도_라우팅하지_않고_예외도_없다()
    {
        var (engine, receivers) = BuildWireOnlyEngine(portCount: 1);
        var ctx = new TestNodeContext(engine);
        var node = new SwitchNode { Id = "sw" };

        var ex = await Record.ExceptionAsync(() => node.OnInputAsync(new Msg { Payload = 1 }, ctx, CancellationToken.None));

        Assert.Null(ex);
        Assert.Empty(receivers[0].Received);
    }
}
