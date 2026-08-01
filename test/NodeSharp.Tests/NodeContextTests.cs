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
/// <see cref="NodeContext"/>(RT-09b, <c>INodeContext</c> 정식 구현체 — 02번 문서 2번 탭 카드9 "정식
/// 통합판" 중 <c>Local</c>/<c>Flow</c>/<c>Global</c>/<c>Env</c> 4개 스코프 + <c>RouteAsync</c>/
/// <c>SetStatus</c> 범위)에 대한 단위 테스트입니다. 완료 기준: 4개 스코프가 같은 key라도 서로 섞이지
/// 않는지, <c>RouteAsync</c>가 <see cref="FlowEngine.RouteAsync"/>로 실제 위임되는지, <c>SetStatus</c>가
/// <see cref="IEventBus"/>로 <see cref="NodeStatusEvent"/>를 발행하는지, <see cref="FlowEngine"/>이 실제
/// 배포 과정에서 만드는 Context도 같은 <see cref="IContextStore"/>를 공유하는지 확인. <c>Shared</c>/
/// <c>Scheduler</c>/<c>Structure</c>는 아직 없어(사용자 확인 완료, 2026-08 세션) 이 테스트 범위 밖입니다.
/// </summary>
public class NodeContextTests
{
    [Fact]
    public void Local_Flow_Global_Env는_같은_key라도_scope가_달라_섞이지_않는다()
    {
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry, store, eventBus);
        var ctx = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");

        ctx.Local.Set("v", "local");
        ctx.Flow.Set("v", "flow");
        ctx.Global.Set("v", "global");
        ctx.Env.Set("v", "env");

        Assert.Equal("local", ctx.Local.Get<string>("v"));
        Assert.Equal("flow", ctx.Flow.Get<string>("v"));
        Assert.Equal("global", ctx.Global.Get<string>("v"));
        Assert.Equal("env", ctx.Env.Get<string>("v"));
    }

    [Fact]
    public void 서로_다른_노드의_NodeContext는_Local_Env는_따로_갖지만_Global은_공유한다()
    {
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry, store, eventBus);
        var ctx1 = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");
        var ctx2 = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n2");

        ctx1.Local.Set("count", 1);
        ctx2.Local.Set("count", 2);
        ctx1.Global.Set("shared", "값");

        Assert.Equal(1, ctx1.Local.Get<int>("count"));
        Assert.Equal(2, ctx2.Local.Get<int>("count"));
        Assert.Equal("값", ctx2.Global.Get<string>("shared"));   // Global은 nodeId와 무관하게 공유됨
    }

    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드 — RouteAsync 위임 확인용.</summary>
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

    [Fact]
    public async Task RouteAsync는_FlowEngine_RouteAsync로_실제_위임된다()
    {
        ReceiverNode.Received.Clear();
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        var engine = new FlowEngine(registry, store, eventBus);
        var n1 = new NodeConfig("n1", "receiver", "발신측(가상)", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var ctx = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");
        await ctx.RouteAsync("n1", 0, new Msg { Payload = "위임됨" }, CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("위임됨", ReceiverNode.Received[0]);
    }

    [Fact]
    public void SetStatus는_EventBus로_NodeStatusEvent를_발행한다()
    {
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry, store, eventBus);
        var ctx = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");

        NodeStatusEvent? received = null;
        using var sub = eventBus.Subscribe<NodeStatusEvent>(e => received = e);

        ctx.SetStatus("green", "dot", "연결됨");

        Assert.NotNull(received);
        Assert.Equal("n1", received!.NodeId);
        Assert.Equal("green", received.Fill);
        Assert.Equal("dot", received.Shape);
        Assert.Equal("연결됨", received.Text);
    }

    [Fact]
    public void SetStatus는_NodeStatusLevel_오버로드로_호출해도_문자열_오버로드와_동일하게_발행된다()
    {
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry, store, eventBus);
        INodeContext ctx = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");

        NodeStatusEvent? received = null;
        using var sub = eventBus.Subscribe<NodeStatusEvent>(e => received = e);

        ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");   // INodeContext 기본 구현이 문자열 오버로드로 위임

        Assert.NotNull(received);
        Assert.Equal("green", received!.Fill);
    }

    /// <summary>OnStartAsync에서 자신의 Local 스코프에 값을 저장하는 테스트 노드 — BuildContext 조립 확인용.</summary>
    private sealed class StoringNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "storing";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
        {
            // FlowEngine.BuildContext가 실제로 NodeContext를 만들어 전달하는지 확인하기 위해 구체
            // 타입으로 캐스팅해 Local 스코프에 값을 저장한다(운영 노드 코드는 보통 INodeContext만 씀).
            if (ctx is NodeContext nc) nc.Local.Set("state", "시작됨");
            return Task.CompletedTask;
        }

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public async Task FlowEngine_BuildContext가_만드는_NodeContext는_FlowEngine_ContextStore와_같은_저장소를_공유한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("storing", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(StoringNode));
        var engine = new FlowEngine(registry);
        var n1 = new NodeConfig("n1", "storing", "저장", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우", Nodes: new[] { n1 }, Wires: Array.Empty<Wire>());

        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);   // OnStartAsync가 Local.Set 호출

        Assert.Equal("시작됨", engine.ContextStore.Get<string>("node", "n1", "state"));
    }
}
