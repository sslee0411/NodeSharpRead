using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// 재배포(Redeploy)와 <see cref="SharedResourceManager"/>(RT-10)의 관계를 검증하는 테스트입니다
/// (RT-10b, 02번 문서 2번 탭 카드3 "재배포와 싱글턴 서비스의 관계" 시퀀스 다이어그램). 완료 기준(03번
/// Step맵 RT-10b): 같은 공유 리소스를 참조하는 TCP-In류 노드가 있는 상태에서 <see cref="DeployMode.ModifiedFlows"/>
/// 재배포를 수행해도(다른 참조가 남아있는 한) 공유 리소스가 끊기지 않는지 확인. <c>NodeContext.Shared</c>
/// 배선은 아직 없어(RT-10 XML 주석 참고, 실제 노드 구현 Step으로 이연) 테스트 노드가 정적 필드로
/// <see cref="SharedResourceManager"/>를 직접 캡처해 <see cref="FlowEngine.DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>
/// 재배포 시나리오를 검증합니다 — 카드2 <c>TcpInNode</c> 예제의 <c>ctx.Shared.AcquireAsync</c>/<c>ReleaseAsync</c>
/// 호출을 <see cref="IFlowNode.OnStartAsync"/>/<see cref="IFlowNode.OnCloseAsync"/> 안에서 그대로 재현합니다.
/// </summary>
public class RedeployVsSharedResourceTests
{
    /// <summary>StartAsync/StopAsync 호출 횟수를 기록하는 가짜 공유 리소스(카드2 예제의 TcpServerNode 역할).</summary>
    private sealed class FakeSharedServer : ISharedServiceNode
    {
        public string Id { get; }
        public int StartCount;
        public int StopCount;

        public FakeSharedServer(string id) => Id = id;

        public Task StartAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref StartCount);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Interlocked.Increment(ref StopCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 카드2 예제의 TcpInNode 역할 — 배포될 때 공유 리소스를 참조(Acquire)하고, 종료될 때 참조를
    /// 해제(Release)한다. <c>ctx.Shared</c> 배선이 아직 없어(RT-10b 범위 밖), 테스트가 정적 필드로 넣어준
    /// <see cref="SharedResourceManager"/>/<see cref="FakeSharedServer"/>를 직접 사용한다.
    /// </summary>
    private sealed class FakeTcpInNode : IFlowNode
    {
        public static SharedResourceManager Manager = null!;
        public static FakeSharedServer Server = null!;

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "fake-tcp-in";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public async Task OnStartAsync(INodeContext ctx, CancellationToken ct) =>
            await Manager.AcquireAsync(Server.Id, () => Server, ct);

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public async Task OnCloseAsync(INodeContext ctx) => await Manager.ReleaseAsync(Server.Id);
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("fake-tcp-in", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(FakeTcpInNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public async Task ModifiedFlows_재배포로_변경된_Flow_탭이_재시작돼도_다른_탭의_참조가_남아있으면_공유_리소스는_끊기지_않는다()
    {
        // 완료 기준 직접 검증: n1(flow-a)/n2(flow-b)가 같은 공유 리소스를 참조한 채 배포된 상태에서,
        // flow-a만 변경돼 ModifiedFlows로 재배포돼도(카드2·카드3의 "TCP-In 3개 중 일부만 재시작" 시나리오와
        // 동일한 원리) flow-b의 n2가 여전히 참조 중이라 StopAsync는 한 번도 불리지 않아야 한다.
        FakeTcpInNode.Manager = new SharedResourceManager();
        FakeTcpInNode.Server = new FakeSharedServer("srv-5000");
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "fake-tcp-in", "TCP-In(A)", "flow-a", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "fake-tcp-in", "TCP-In(B)", "flow-b", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        Assert.Equal(1, FakeTcpInNode.Server.StartCount);   // n1/n2 둘 다 참조했지만 실제 시작은 1회

        var n1Changed = n1 with { Name = "TCP-In(A) 변경됨" };
        var flow2 = flow1 with { Nodes = new[] { n1Changed, n2 } };
        await engine.DeployAsync(flow2, DeployMode.ModifiedFlows, CancellationToken.None);   // flow-a만 재시작, flow-b는 그대로

        Assert.Equal(0, FakeTcpInNode.Server.StopCount);    // n2가 여전히 참조 중 — 한 번도 끊기지 않음
        Assert.Equal(1, FakeTcpInNode.Server.StartCount);   // n1이 Release 후 다시 Acquire해도 이미 등록돼 있어 재시작 없음
    }

    [Fact]
    public async Task ModifiedNodes_재배포로_한_노드만_재시작돼도_같은_탭의_다른_노드가_참조_중이면_공유_리소스는_끊기지_않는다()
    {
        FakeTcpInNode.Manager = new SharedResourceManager();
        FakeTcpInNode.Server = new FakeSharedServer("srv-5000");
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "fake-tcp-in", "TCP-In #1", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "fake-tcp-in", "TCP-In #2", "f1", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        var n1Changed = n1 with { Name = "TCP-In #1 변경됨" };
        var flow2 = flow1 with { Nodes = new[] { n1Changed, n2 } };
        await engine.DeployAsync(flow2, DeployMode.ModifiedNodes, CancellationToken.None);   // n1만 재시작, n2는 인스턴스 유지

        Assert.Equal(0, FakeTcpInNode.Server.StopCount);
    }

    [Fact]
    public async Task 마지막까지_참조하던_노드가_모두_사라지면_공유_리소스는_실제로_해제된다()
    {
        // 대조군: 참조가 정말 0이 되면 StopAsync가 실제로 호출되는지 확인 — 위 두 테스트가 "끊기지 않음"만
        // 보여주는 것이 우연이 아니라 SharedResourceManager 참조 카운트가 정확히 동작한 결과임을 함께 증명한다.
        FakeTcpInNode.Manager = new SharedResourceManager();
        FakeTcpInNode.Server = new FakeSharedServer("srv-5000");
        var engine = BuildEngine();
        var n1 = new NodeConfig("n1", "fake-tcp-in", "TCP-In #1", "f1", new Dictionary<string, object?>());
        var n2 = new NodeConfig("n2", "fake-tcp-in", "TCP-In #2", "f1", new Dictionary<string, object?>());
        var flow1 = new FlowDefinition(Id: "proj", Name: "테스트", Nodes: new[] { n1, n2 }, Wires: Array.Empty<Wire>());
        await engine.DeployAsync(flow1, DeployMode.Full, CancellationToken.None);

        var flow2 = flow1 with { Nodes = Array.Empty<NodeConfig>() };   // n1/n2 둘 다 삭제
        await engine.DeployAsync(flow2, DeployMode.Full, CancellationToken.None);

        Assert.Equal(1, FakeTcpInNode.Server.StopCount);   // 참조가 0이 된 이후 정확히 1번만 해제
    }
}
