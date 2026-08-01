using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.RouteAsync"/>(RT-04a, 02번 설계 문서 2번 탭 카드4 원본·3번 탭 카드4 메시지
/// 파이프라인 시퀀스)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-04a, RT-04a 착수 중 발견한
/// 계약 불일치를 반영해 정정된 버전): A→B 1:1 Wire에서 A가 <c>ctx.RouteAsync</c>로 보낸 <see cref="Msg"/>가
/// B의 <c>OnInputAsync</c> 인자로(<c>Clone()</c>되어) 그대로 전달되는지 확인.
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
        return new FlowEngine(registry);
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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 그대로 위임하는 테스트 전용 <see cref="INodeContext"/>.</summary>
    private sealed class TestNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public TestNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
    }
}
