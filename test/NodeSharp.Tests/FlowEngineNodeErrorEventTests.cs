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
/// (LK-04) <see cref="FlowEngine.DispatchOneAsync"/>가 대상 노드의 <c>OnInputAsync</c> 예외를 흡수해
/// <see cref="NodeErrorEvent"/>를 발행하는 동작에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 LK-04)의
/// "의도적으로 에러를 발생시키는 Function 노드를 배포한 뒤 ... 에러 발생 노드와 해당 시점 Msg 내용까지
/// 역추적 가능한지"를 뒷받침합니다: ① 예외가 나면 <see cref="NodeErrorEvent"/>가 발행되고 그 필드가
/// 노드 정보·예외 정보·에러 시점 msg 스냅샷을 정확히 담는지 ② 예외가 <c>RouteAsync</c> 호출부까지
/// 전파되지 않는지(격리) ③ 한 Wire의 예외가 같은 배치의 다른 Wire 전달을 막지 않는지(Fan-out 격리)
/// ④ 정상 경로(예외 없음)에서는 <see cref="NodeErrorEvent"/>가 전혀 발행되지 않는지.
/// <see cref="FlowEngineRouteAsyncTests"/>와 동일하게 테스트마다 독립된 <see cref="EventBus"/>를
/// 주입해 다른 테스트와 이벤트가 섞이지 않게 합니다.
/// </summary>
public class FlowEngineNodeErrorEventTests
{
    /// <summary>입력을 받으면 항상 예외를 던지는 테스트 전용 노드 — Function 노드가 사용자 표현식에서 예외를 던지는 상황을 흉내낸다.</summary>
    private sealed class ThrowingNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "throwing-function";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("의도적인 테스트 예외");

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드 — 격리 확인(다른 Wire는 계속 전달되는지)용.</summary>
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

    private static (FlowEngine Engine, EventBusAdapter EventBus) BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("throwing-function", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ThrowingNode));
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        // (FlowEngineRouteAsyncTests와 동일한 원칙) 테스트마다 독립된 EventBus를 명시 주입해 교차 오염 방지.
        var eventBus = new EventBusAdapter(new EventBus());
        return (new FlowEngine(registry, eventBus: eventBus), eventBus);
    }

    [Fact]
    public async Task OnInputAsync가_예외를_던지면_NodeErrorEvent가_노드정보와_msg스냅샷을_담아_발행된다()
    {
        var (engine, eventBus) = BuildEngine();
        var received = new List<NodeErrorEvent>();
        using var subscription = eventBus.Subscribe<NodeErrorEvent>(received.Add);

        var source = new NodeConfig("n0", "receiver", "발신원", "f1", new Dictionary<string, object?>());
        var n1 = new NodeConfig("n1", "throwing-function", "폭발노드", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { source, n1 },
            Wires: new[] { new Wire(SourceNodeId: "n0", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n0", 0, new Msg { Payload = "위험값" }, CancellationToken.None);

        Assert.Single(received);
        var evt = received[0];
        Assert.Equal("n1", evt.NodeId);
        Assert.Equal("폭발노드", evt.NodeName);
        Assert.Equal("throwing-function", evt.NodeType);
        Assert.Equal(nameof(InvalidOperationException), evt.ExceptionType);
        Assert.Equal("의도적인 테스트 예외", evt.Message);
        Assert.Contains("위험값", evt.MsgSnapshotJson);
        Assert.False(string.IsNullOrEmpty(evt.MsgId));
    }

    [Fact]
    public async Task OnInputAsync_예외는_RouteAsync_호출부까지_전파되지_않는다()
    {
        var (engine, _) = BuildEngine();
        var source = new NodeConfig("n0", "receiver", "발신원", "f1", new Dictionary<string, object?>());
        var n1 = new NodeConfig("n1", "throwing-function", "폭발노드", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { source, n1 },
            Wires: new[] { new Wire(SourceNodeId: "n0", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            engine.RouteAsync("n0", 0, new Msg(), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task 한_Wire의_예외가_같은_배치의_다른_Wire_전달을_막지_않는다()
    {
        ReceiverNode.Received.Clear();
        var (engine, _) = BuildEngine();
        var thrower = new NodeConfig("n1", "throwing-function", "폭발노드", "f1", new Dictionary<string, object?>());
        var receiver = new NodeConfig("n2", "receiver", "수신노드", "f1", new Dictionary<string, object?>());
        var source = new NodeConfig("n0", "receiver", "발신원", "f1", new Dictionary<string, object?>()); // 타입은 무관, Wires 출발점 표기용
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { source, thrower, receiver },
            Wires: new[]
            {
                new Wire(SourceNodeId: "n0", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0),
                new Wire(SourceNodeId: "n0", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
            });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n0", 0, new Msg { Payload = "동시전달" }, CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("동시전달", ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task 정상_경로에서는_NodeErrorEvent가_발행되지_않는다()
    {
        ReceiverNode.Received.Clear();
        var (engine, eventBus) = BuildEngine();
        var received = new List<NodeErrorEvent>();
        using var subscription = eventBus.Subscribe<NodeErrorEvent>(received.Add);

        var source = new NodeConfig("n0", "receiver", "발신원", "f1", new Dictionary<string, object?>());
        var receiver = new NodeConfig("n2", "receiver", "수신노드", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { source, receiver },
            Wires: new[] { new Wire(SourceNodeId: "n0", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        await engine.RouteAsync("n0", 0, new Msg { Payload = "정상값" }, CancellationToken.None);

        Assert.Empty(received);
        Assert.Single(ReceiverNode.Received);
    }
}
