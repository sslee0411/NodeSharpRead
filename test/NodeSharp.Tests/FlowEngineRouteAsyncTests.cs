using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.RouteAsync"/>(RT-04a, 02번 설계 문서 2번 탭 카드4 원본·3번 탭 카드4 메시지
/// 파이프라인 시퀀스)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-04a, RT-04a 착수 중 발견한
/// 계약 불일치를 반영해 정정된 버전): A→B 1:1 Wire에서 A가 <c>ctx.RouteAsync</c>로 보낸 <see cref="Msg"/>가
/// B의 <c>OnInputAsync</c> 인자로(<c>Clone()</c>되어) 그대로 전달되는지 확인.
/// (LK-02a) <see cref="FlowActivityEvent"/> 발행(<c>DispatchOneAsync</c>가 대상을 찾은 직후 발행) 테스트
/// 4건도 이 파일에 함께 둔다 — <c>RouteAsync</c>가 만드는 바로 그 디스패치 경로를 검증 대상으로 삼기
/// 때문에 별도 파일보다 이 파일이 자연스럽다.
/// </summary>
public class FlowEngineRouteAsyncTests
{
    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드 — RouteAsync 전달 여부 확인용.</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public static readonly List<(string NodeName, object? Payload)> Received = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "receiver";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            Received.Add((Name, msg.Payload));
            return Task.CompletedTask;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>
    /// 예제(카드1 <c>PassThroughNode</c>)와 동일하게, 입력을 받으면 <c>ctx.RouteAsync</c>로 그대로
    /// 다음 노드에 전달하는 패스스루 노드 — "OnInputAsync가 ctx.RouteAsync를 직접 호출" 콜백 계약을
    /// 실제로 행사하는 테스트 노드.
    /// </summary>
    private sealed class PassThroughNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "pass-through";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
            ctx.RouteAsync(Id, 0, msg, ct);

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        registry.TryRegister(new PluginManifest("pass-through", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(PassThroughNode));
        // (LK-02a 버그 수정) eventBus를 생략하면 FlowEngine이 기본값으로 new EventBusAdapter()를 만드는데,
        // 그 기본 생성자는 프로세스 전역 EventBus.Instance를 감싼다 — 이 테스트가 FlowActivityEvent를
        // 구독하기 전까지는 아무도 구독하지 않아 문제가 드러나지 않았지만, LK-02a에서 실제로 구독을
        // 시작하자 같은 프로세스에서 병렬 실행되는 다른 테스트(다른 클래스의 RouteAsync 호출 포함)가
        // 발행한 이벤트까지 함께 잡혀버리는 교차 오염이 발생했다(사용자 보고: Parallel 테스트가 기대한
        // 2건 대신 5건을 받음). EventBusAdapter 클래스 문서가 이미 권장하는 대로, 테스트마다 독립된
        // EventBus 인스턴스를 명시적으로 주입해 이 파일의 모든 테스트를 서로·다른 파일과 격리한다.
        return new FlowEngine(registry, eventBus: new EventBusAdapter(new EventBus()));
    }

    [Fact]
    public async Task RouteAsync는_1대1_Wire를_따라_대상_노드의_OnInputAsync로_Msg를_전달한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = 42 }, CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal(("수신", (object?)42), ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task RouteAsync는_대상에게_원본이_아닌_Clone된_Msg를_전달해_변경이_격리된다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var original = new Msg { Payload = "원본" };
        await engine.RouteAsync("n1", 0, original, CancellationToken.None);
        original.Payload = "발신측에서_이후_변경";   // 전달 후 원본을 바꿔도 이미 전달된 값에는 영향 없어야 함

        Assert.Equal("원본", ReceiverNode.Received[0].Payload);
    }

    [Fact]
    public async Task RouteAsync는_일치하는_Wire가_없으면_아무_노드에도_전달하지_않는다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        // n1의 0번 포트가 아니라 1번 포트에서 나가는 Wire만 있음 — 0번 포트로 RouteAsync하면 대상이 없어야 함
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 1, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "무시됨" }, CancellationToken.None);

        Assert.Empty(ReceiverNode.Received);
    }

    [Fact]
    public async Task RouteAsync는_배포_전에_호출해도_예외_없이_아무일도_하지_않는다()
    {
        var engine = BuildEngine();

        var ex = await Record.ExceptionAsync(() => engine.RouteAsync("n1", 0, new Msg { Payload = 1 }, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task RouteAsync는_대상_노드가_MissingNode로_남아있어도_예외_없이_건너뛴다()
    {
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "no-such-type", "삭제된 플러그인", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);   // n2는 MissingNode로 배포됨

        var ex = await Record.ExceptionAsync(() => engine.RouteAsync("n1", 0, new Msg { Payload = 1 }, CancellationToken.None));

        Assert.Null(ex);   // MissingNode.OnInputAsync는 입력을 그냥 버리므로 예외 없이 통과
    }

    [Fact]
    public async Task PassThroughNode의_OnInputAsync는_ctx_RouteAsync로_받은_Msg를_그대로_넘긴다()
    {
        // ★ RT-04a 범위 밖 주의: PassThroughNode.OnInputAsync는 02번 문서 2번 탭 카드1 예제와 동일하게
        //   ctx.RouteAsync(Id, ...)를 호출하지만, IFlowNode.Id는 아직 NodeConfig.Id와 동기화되지 않는다
        //   (RT-01a에서 RG-01로 의도적으로 미룸 — Activator.CreateInstance가 만든 인스턴스는 매번 새
        //   Guid를 자체 Id로 가짐). 그래서 이 테스트는 "n1→n2 Wire를 통한 연쇄 전달"이 아니라, 엔진
        //   RouteAsync에 실제 노드 인스턴스를 대상으로 지정했을 때 그 노드의 OnInputAsync 콜백이
        //   ctx.RouteAsync를 정상 호출하는지(예외 없이 완료되는지)만 확인한다 — 노드 자기 Id 기반의
        //   Wire 매칭 연쇄 시나리오는 RG-01 완료 후 재검증 필요(신규 발견 아님, RT-01a Ver History 참고).
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "중계", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우", Nodes: new[] { n1 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var ex = await Record.ExceptionAsync(async () =>
        {
            var ctx = new TestNodeContext(engine);
            await engine.Nodes["n1"].OnInputAsync(new Msg { Payload = "통과" }, ctx, CancellationToken.None);
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task RouteAsync는_대상을_찾으면_FlowActivityEvent를_올바른_필드로_발행한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var received = new List<FlowActivityEvent>();
        using var sub = engine.EventBus.Subscribe<FlowActivityEvent>(received.Add);

        var msg = new Msg { Payload = 1 };
        await engine.RouteAsync("n1", 0, msg, CancellationToken.None);

        var e = Assert.Single(received);
        Assert.Equal("n1", e.FromNodeId);
        Assert.Equal(0, e.OutputPort);
        Assert.Equal("n2", e.ToNodeId);
        Assert.Equal(msg.Id, e.MsgId);
    }

    [Fact]
    public async Task RouteAsync는_일치하는_Wire가_없으면_FlowActivityEvent도_발행하지_않는다()
    {
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 1, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var received = new List<FlowActivityEvent>();
        using var sub = engine.EventBus.Subscribe<FlowActivityEvent>(received.Add);

        await engine.RouteAsync("n1", 0, new Msg { Payload = "무시됨" }, CancellationToken.None);

        Assert.Empty(received);
    }

    [Fact]
    public async Task RouteAsync는_대상이_MissingNode여도_FlowActivityEvent는_발행한다()
    {
        // (LK-02a) FlowEngine.cs 클래스 remarks의 "MissingNode도 _nodes에 실제로 들어 있어 이 조건을
        // 통과한다" 설명을 직접 검증 — Wire 자체는 배포에 존재하므로 캔버스 와이어는 하이라이트되는
        // 것이 의도된 동작이다(대상 노드가 무동작인 것과 별개).
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "no-such-type", "삭제된 플러그인", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);   // n2는 MissingNode로 배포됨

        var received = new List<FlowActivityEvent>();
        using var sub = engine.EventBus.Subscribe<FlowActivityEvent>(received.Add);

        await engine.RouteAsync("n1", 0, new Msg { Payload = 1 }, CancellationToken.None);

        var e = Assert.Single(received);
        Assert.Equal("n2", e.ToNodeId);
    }

    [Fact]
    public async Task RouteAsync는_Parallel_디스패치에서도_와이어마다_FlowActivityEvent를_발행한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine();
        // OutputDispatch: Parallel — n1의 0번 포트에서 n2/n3 두 곳으로 동시에 나가는 Fan-out(RT-04b).
        var n1 = new NodeConfig("n1", "pass-through", "발신", "f1", new Dictionary<string, object?>(), OutputDispatch: DispatchMode.Parallel);
        var n2 = new NodeConfig("n2", "receiver", "수신1", "f1", new Dictionary<string, object?>());
        var n3 = new NodeConfig("n3", "receiver", "수신2", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2, n3 },
            Wires: new[]
            {
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0),
            });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var received = new List<FlowActivityEvent>();
        var gate = new object();
        // Parallel 경로라 여러 스레드에서 동시에 Add할 수 있어 lock으로 보호(테스트 자체의 스레드
        // 안전성 문제일 뿐 FlowEngine의 동작과는 무관).
        using var sub = engine.EventBus.Subscribe<FlowActivityEvent>(e =>
        {
            lock (gate) { received.Add(e); }
        });

        await engine.RouteAsync("n1", 0, new Msg { Payload = "fan-out" }, CancellationToken.None);

        Assert.Equal(2, received.Count);
        Assert.Contains(received, e => e.ToNodeId == "n2");
        Assert.Contains(received, e => e.ToNodeId == "n3");
    }

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 그대로 위임하는 테스트 전용 <see cref="INodeContext"/>.</summary>
    private sealed class TestNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public TestNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }

        // (NR-04) INodeContext.Flow/Global 신규 멤버 — 이 파일은 이미 NodeSharp.Runtime을 참조하므로
        // 실제 구현체 ContextScope+InMemoryContextStore를 그대로 재사용(별도 스텁 불필요).
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);

        // (NR-11) INodeContext.Debug 신규 멤버 — 이 파일의 테스트 범위(RouteAsync)와 무관해 무동작.
        public void Debug(string nodeName, string msgJson) { }
    }
}
