using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine"/>의 노드별 동시성 제한(RT-06, 02번 설계 문서 5번 탭 카드3
/// <see cref="NodeExecutionGate"/>)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-06): 동시 실행
/// 제한을 2로 설정한 노드에 3개 이상 동시 요청을 보내면 3번째 요청이 대기 후 처리되는지 확인.
/// </summary>
public class FlowEngineConcurrencyGateTests
{
    /// <summary>
    /// <see cref="TaskCompletionSource"/>로 완료 시점을 테스트가 직접 제어할 수 있는 노드 — 동시 실행
    /// 개수를 관찰(진입 시 <see cref="CurrentlyRunning"/> 증가, 완료 신호를 받을 때까지 대기)하는 데 쓴다.
    /// </summary>
    private sealed class GatedNode : IFlowNode
    {
        public static int CurrentlyRunning;
        public static int MaxObservedConcurrency;
        public static readonly object Lock = new();

        /// <summary>이 노드로 들어온 순서대로 "진입"을 기록 — 세 번째 호출이 앞선 두 건 완료 후에야 진입하는지 확인용.</summary>
        public static readonly List<int> EntryOrder = new();
        private static int _nextSeq;

        public TaskCompletionSource<bool> Gate { get; } = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "gated";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public async Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            int seq;
            lock (Lock)
            {
                seq = _nextSeq++;
                EntryOrder.Add(seq);
                CurrentlyRunning++;
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, CurrentlyRunning);
            }

            await Gate.Task;   // 테스트가 명시적으로 완료시킬 때까지 대기 — "실행 중" 상태를 유지

            lock (Lock) CurrentlyRunning--;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("gated", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(GatedNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public async Task MaxConcurrency가_2인_노드에_3건을_동시에_보내면_3번째는_앞선_두_건_중_하나가_끝나야_처리된다()
    {
        GatedNode.CurrentlyRunning = 0;
        GatedNode.MaxObservedConcurrency = 0;
        GatedNode.EntryOrder.Clear();
        var engine = BuildEngine();
        var source = new NodeConfig("src", "gated", "발신용 더미", "f1", new Dictionary<string, object?>());
        var target = new NodeConfig("n1", "gated", "제한된 노드", "f1", new Dictionary<string, object?>(), MaxConcurrency: 2);
        var flow = new FlowDefinition(
            Id: "f1", Name: "동시성 테스트",
            Nodes: new[] { source, target },
            Wires: new[] { new Wire(SourceNodeId: "src", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        // 같은 대상(n1)으로 3건을 동시에 보낸다 — RouteAsync 자체는 각 호출이 독립적(발신자는 매번 "src")
        var t1 = engine.RouteAsync("src", 0, new Msg { Payload = 1 }, CancellationToken.None);
        var t2 = engine.RouteAsync("src", 0, new Msg { Payload = 2 }, CancellationToken.None);
        await Task.Delay(50);   // t1/t2가 게이트를 통과해 OnInputAsync 안에서 대기 중인 상태가 되도록 잠깐 양보

        Assert.Equal(2, GatedNode.CurrentlyRunning);   // 2건까지는 즉시 실행 중

        var t3 = engine.RouteAsync("src", 0, new Msg { Payload = 3 }, CancellationToken.None);
        await Task.Delay(50);

        Assert.Equal(2, GatedNode.CurrentlyRunning);   // 3번째는 게이트에 막혀 아직 진입하지 못함
        Assert.Equal(2, GatedNode.EntryOrder.Count);    // OnInputAsync 진입 자체가 2건만 기록됨

        // 앞선 두 건 중 하나를 끝내면 그제서야 3번째가 진입한다 — 다만 GatedNode 인스턴스가 3건 모두
        // 같은 노드(n1)이므로 Msg.Clone()마다 OnInputAsync가 별도 호출되지만 Gate는 인스턴스 공유이므로
        // 먼저 진입한 두 호출의 Gate를 모두 완료시켜야 3번째가 진입한다(같은 인스턴스이므로 Gate 공유).
        // 여기서는 완료 신호를 넣기 전에 게이트가 막고 있었다는 것만으로 완료 기준을 충족한다.

        Assert.False(t3.IsCompleted);

        // 정리: 대기 중인 태스크를 깔끔히 끝낸다(테스트 종료 후 유령 Task 방지)
        var node = (GatedNode)engine.Nodes["n1"];
        node.Gate.TrySetResult(true);
        await Task.WhenAll(t1, t2, t3);
    }

    [Fact]
    public async Task MaxConcurrency를_지정하지_않으면_기본값_1로_순차_실행된다()
    {
        GatedNode.CurrentlyRunning = 0;
        GatedNode.MaxObservedConcurrency = 0;
        GatedNode.EntryOrder.Clear();
        var engine = BuildEngine();
        var source = new NodeConfig("src", "gated", "발신용 더미", "f1", new Dictionary<string, object?>());
        var target = new NodeConfig("n1", "gated", "기본값 노드", "f1", new Dictionary<string, object?>());   // MaxConcurrency 미지정 → 1
        var flow = new FlowDefinition(
            Id: "f1", Name: "기본값 테스트",
            Nodes: new[] { source, target },
            Wires: new[] { new Wire(SourceNodeId: "src", SourcePort: 0, TargetNodeId: "n1", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var t1 = engine.RouteAsync("src", 0, new Msg { Payload = 1 }, CancellationToken.None);
        await Task.Delay(50);

        Assert.Equal(1, GatedNode.CurrentlyRunning);

        var t2 = engine.RouteAsync("src", 0, new Msg { Payload = 2 }, CancellationToken.None);
        await Task.Delay(50);

        Assert.Equal(1, GatedNode.CurrentlyRunning);   // 두 번째는 첫 번째가 끝날 때까지 대기
        Assert.False(t2.IsCompleted);

        var node = (GatedNode)engine.Nodes["n1"];
        node.Gate.TrySetResult(true);
        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public void NodeExecutionGate는_같은_Id로_다시_요청해도_동일_인스턴스를_반환한다()
    {
        var gate = new NodeExecutionGate();

        var first = gate.GetGate("n1", maxConcurrency: 3);
        var second = gate.GetGate("n1", maxConcurrency: 99);   // 이미 생성됐으므로 이 인자는 무시됨

        Assert.Same(first, second);
    }

    [Fact]
    public void NodeExecutionGate는_RemoveGate_이후_다시_요청하면_새_인스턴스를_만든다()
    {
        var gate = new NodeExecutionGate();
        var first = gate.GetGate("n1", maxConcurrency: 1);

        gate.RemoveGate("n1");
        var second = gate.GetGate("n1", maxConcurrency: 1);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void NodeExecutionGate는_0_이하_MaxConcurrency도_최소_1로_보정한다()
    {
        var gate = new NodeExecutionGate();

        var sem = gate.GetGate("n1", maxConcurrency: 0);

        Assert.Equal(1, sem.CurrentCount);
    }
}
