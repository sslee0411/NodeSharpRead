using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.RouteAsync"/>의 순환 구조 hop-count 안전장치(RT-05, 02번 설계 문서 5번 탭
/// 카드2)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-05): A→B→A 순환 Wire를 구성해 배포했을
/// 때 hop-count가 임계값을 넘으면 무한루프 없이 중단되고 경고가 기록되는지 확인(<c>EventBus</c>가
/// 아직 없어 <c>NodeErrorEvent</c> 대신 <see cref="FlowEngine.LoopGuardTrips"/>로 검증).
/// </summary>
public class FlowEngineLoopGuardTests
{
    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드.</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public static readonly List<int> ReceivedHopCounts = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "receiver";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            lock (ReceivedHopCounts) ReceivedHopCounts.Add(msg.HopCount);
            return Task.CompletedTask;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>
    /// 입력을 받으면 다시 <c>ctx.RouteAsync</c>로 되돌려 보내는 테스트 전용 노드 — A→B→A 순환을
    /// 실제로 재귀 호출 체인으로 구현해 hop-count 가드가 무한 재귀를 막는지 검증하는 데 쓴다.
    /// <see cref="RouteFromId"/>는 배포 후 테스트가 지정하는 발신 Id — <see cref="IFlowNode.Id"/>가 아직
    /// <c>NodeConfig.Id</c>와 동기화되지 않아(RG-01 대기, RT-01a Ver History 참고) 실제 배포된
    /// <c>NodeConfig.Id</c>를 대신 담아 <c>ctx.RouteAsync</c> 호출에 사용한다(RT-04a 테스트와 동일한 처리).
    /// </summary>
    private sealed class LoopingNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "looping";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

        public string RouteFromId { get; set; } = string.Empty;

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
            ctx.RouteAsync(RouteFromId, 0, msg, ct);

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        registry.TryRegister(new PluginManifest("looping", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(LoopingNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public void MaxHopCount_기본값은_1000이다()
    {
        var engine = BuildEngine();

        Assert.Equal(1000, engine.MaxHopCount);
    }

    [Fact]
    public async Task RouteAsync는_전달할_때마다_HopCount를_1씩_증가시킨다()
    {
        ReceiverNode.ReceivedHopCounts.Clear();
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "looping", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var msg = new Msg { Payload = 1 };
        Assert.Equal(0, msg.HopCount);

        await engine.RouteAsync("n1", 0, msg, CancellationToken.None);

        Assert.Single(ReceiverNode.ReceivedHopCounts);
        Assert.Equal(1, ReceiverNode.ReceivedHopCounts[0]);   // 대상이 받은 Clone은 HopCount 1을 그대로 물려받음
    }

    [Fact]
    public async Task HopCount가_MaxHopCount_이상이면_전달을_차단하고_LoopGuardTrips에_기록한다()
    {
        ReceiverNode.ReceivedHopCounts.Clear();
        var engine = BuildEngine();
        engine.MaxHopCount = 5;
        var n1 = new NodeConfig("n1", "looping", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var msg = new Msg { Payload = "이미_임계값_도달" };
        msg.HopCount = 5;   // engine.MaxHopCount와 동일 — 즉시 차단돼야 함

        await engine.RouteAsync("n1", 0, msg, CancellationToken.None);

        Assert.Empty(ReceiverNode.ReceivedHopCounts);   // 대상에게 전달되지 않음
        Assert.Single(engine.LoopGuardTrips);
        Assert.Equal(("n1", msg.Id), engine.LoopGuardTrips[0]);
    }

    [Fact]
    public async Task A_B_순환_Wire에서_HopCount가_MaxHopCount를_넘으면_무한재귀_없이_중단된다()
    {
        var engine = BuildEngine();
        engine.MaxHopCount = 10;   // 빠른 테스트를 위해 작게 설정

        // ★(발견한 공백, RT-05/RT-06 상호작용) NodeExecutionGate(RT-06)의 노드별 동시 실행 제한
        // (MaxConcurrency 기본값 1)과 이 테스트의 재귀형 순환 라우팅(A→B→A→B→...)이 함께 있으면
        // 교착상태(deadlock)가 생긴다 — RouteAsync가 매 홉을 "직접 재귀 호출(중첩 await)"로 처리하기
        // 때문에, 같은 노드를 다시 거치는 시점에는 그 노드의 이전 호출이 아직 끝나지 않은 채로
        // 게이트를 다시 요청하게 된다. 게이트 입장에서는 "동시에 2번 호출됨"으로 보여 두 번째 요청을
        // 막는데, 그 두 번째 호출이 끝나야 첫 번째도 끝날 수 있는 구조라 서로가 서로를 막아 영원히
        // 대기하게 된다(HopCount가 MaxHopCount에 도달하기도 전에 멈춤). 이 테스트는 hop-count 가드
        // 자체를 검증하는 것이 목적이므로, MaxConcurrency를 이 재귀 깊이(MaxHopCount=10, 각 노드를
        // 최대 5번 정도 거침)보다 충분히 크게(20) 줘서 게이트가 막지 않게 우회했다. 게이트와 재귀
        // 라우팅이 실제로 충돌할 수 있다는 사실 자체는 근본 해결이 필요한 별도 공백으로 README에
        // 기록하고, 이번 테스트에서는 hop-count 가드 검증만 다룬다.
        var n1 = new NodeConfig("n1", "looping", "A", "f1", new Dictionary<string, object?>(), MaxConcurrency: 20);
        var n2 = new NodeConfig("n2", "looping", "B", "f1", new Dictionary<string, object?>(), MaxConcurrency: 20);
        var flow = new FlowDefinition(
            Id: "f1", Name: "순환 테스트",
            Nodes: new[] { n1, n2 },
            Wires: new[]
            {
                new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
                new Wire(SourceNodeId: "n2", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0),
            });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);
        ((LoopingNode)engine.Nodes["n1"]).RouteFromId = "n1";
        ((LoopingNode)engine.Nodes["n2"]).RouteFromId = "n2";

        // A→B→A→B→... 재귀 체인이 실제로 발생한다. 가드가 없으면 콜스택이 끝없이 쌓여 StackOverflow가
        // 나거나 테스트가 영원히 끝나지 않아야 하지만, MaxHopCount 가드 덕분에 예외 없이 종료돼야 한다.
        var ex = await Record.ExceptionAsync(() =>
            engine.RouteAsync("n1", 0, new Msg { Payload = "루프" }, CancellationToken.None));

        Assert.Null(ex);
        Assert.NotEmpty(engine.LoopGuardTrips);   // 어딘가에서 반드시 가드가 발동해야 함
    }

    [Fact]
    public async Task HopCount가_MaxHopCount_미만이면_정상_전달된다()
    {
        ReceiverNode.ReceivedHopCounts.Clear();
        var engine = BuildEngine();
        engine.MaxHopCount = 5;
        var n1 = new NodeConfig("n1", "looping", "발신", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { n1, n2 },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var msg = new Msg { Payload = "정상" };
        msg.HopCount = 4;   // MaxHopCount(5) 미만 — 차단되면 안 됨

        await engine.RouteAsync("n1", 0, msg, CancellationToken.None);

        Assert.Single(ReceiverNode.ReceivedHopCounts);
        Assert.Empty(engine.LoopGuardTrips);
    }
}
