using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// 플로우 실행 엔진. Editor(미리보기 실행)와 Runner(운영 실행) 양쪽이 공유하는 순수 로직입니다
/// (WPF 비의존, 1번 탭 카드2). 이 클래스는 <c>RT-01~11</c>에 걸쳐 증분으로 완성됩니다 — 지금까지는
/// <see cref="CreateInstance"/>(<c>RT-01a</c>)와 Full 모드 <see cref="DeployAsync"/>(<c>RT-01b</c>)만
/// 있습니다. 노드별 예외 격리·MissingNode 대체(<c>RT-02a/b</c>), 부분 재배포(<c>RT-03</c>), 메시지
/// 라우팅(<c>RouteAsync</c>, <c>RT-04a</c>) 등은 아직 없습니다("뼈대 우선, 확장" 원칙, 03번 Step맵 카드1).
/// 설계 근거: 02번 문서 2번 탭 카드 4·카드 9(정식 기준본), 3번 탭 카드 3(노드 생명주기 시퀀스)·카드 6
/// (배포 예외 격리 — <c>BuildContext</c> 참조부).
/// </summary>
/// <remarks>
/// <see cref="_registry"/>가 실제 인스턴스 생성을 담당합니다(<c>NodeSharp.Registry.NodeTypeRegistry</c>,
/// <c>CT-06b</c>+<c>RT-01a</c>) — <see cref="FlowEngine"/> 자체는 타입 조회 방법을 모르고 <see cref="INodeRegistry"/>
/// 계약에만 의존합니다.
/// <para>
/// (★ RT-01b) <see cref="DeployAsync"/>는 02번 문서 2번 탭 카드4 원본 스니펫처럼 <b>두 단계</b>로 나뉩니다 —
/// 먼저 <see cref="FlowDefinition.Nodes"/> 전체를 <see cref="CreateInstance"/>로 생성해 <see cref="Nodes"/>에
/// 채운 뒤(1단계), 그 다음에야 생성된 노드 전체의 <c>OnStartAsync</c>를 순서대로 호출합니다(2단계) —
/// 완료 기준이 요구하는 "CreateInstance→OnStartAsync 순으로, 순서가 뒤바뀌지 않고 호출"을 이 두 단계
/// 구조로 보장합니다. <see cref="Nodes"/>는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>로
/// 키를 삼습니다 — <c>RT-01a</c>에서 <c>IFlowNode.Id</c>↔<c>NodeConfig.Id</c> 동기화를 의도적으로
/// <c>RG-01</c>로 미뤄뒀기 때문에(<c>Activator.CreateInstance</c>로 만든 노드는 자체 Id를 가질 수 있음),
/// <c>Wire.SourceNodeId</c>/<c>TargetNodeId</c>(2번 탭 카드2)가 참조하는 안정적인 식별자인
/// <see cref="NodeConfig.Id"/>로 관리해야 이후 <c>RT-04a</c>(메시지 라우팅)가 Wire 기반 조회를 그대로
/// 쓸 수 있습니다.
/// </para>
/// <para>
/// (★ RT-01b) <see cref="BuildContext"/>는 02번 문서 3번 탭 카드6·2번 탭 카드8(2602행)에 <c>BuildContext(node)</c>
/// 호출부만 있고 정식 선언이 없던 공백입니다(<c>NodeRef</c>와 동일 유형) — 지금은 <c>NodeContext</c>
/// (Runtime 구체 클래스, <c>RT-09</c>)가 아직 없어 <see cref="INodeContext"/>의 임시 무동작(no-op) 구현인
/// <see cref="NoOpNodeContext"/>를 반환합니다. <c>RT-09</c>에서 실제 <c>NodeContext</c>로 교체될 때까지의
/// 임시 자리표시자이며, <c>RT-02a</c>의 <c>MissingNode</c>와 동일한 "타입 시스템을 만족시키는 최소 스텁"
/// 성격입니다.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
/// registry.TryRegister(new PluginManifest("inject", "1.0.0", "1.0.0"), typeof(InjectNode));
/// var engine = new FlowEngine(registry);
///
/// // RT-01a — 단일 인스턴스 생성
/// var cfg = new NodeConfig("n1", "inject", "타이머", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = engine.CreateInstance(cfg);
///
/// // RT-01b — Full 모드 배포(노드 3개 이상, CreateInstance 전체 → OnStartAsync 순서대로)
/// var flow = new FlowDefinition("f1", "테스트", Nodes: new[] { cfg }, Wires: Array.Empty&lt;Wire&gt;());
/// await engine.DeployAsync(flow, CancellationToken.None);
/// IFlowNode deployed = engine.Nodes["n1"];   // NodeConfig.Id로 조회(IFlowNode.Id 아님)
/// </code>
/// </example>
public sealed class FlowEngine
{
    private readonly INodeRegistry _registry;
    private readonly Dictionary<string, IFlowNode> _nodes = new();

    /// <summary>노드 타입 조회·인스턴스 생성을 위임할 레지스트리를 받아 엔진을 생성합니다.</summary>
    public FlowEngine(INodeRegistry registry) => _registry = registry;

    /// <summary>
    /// <paramref name="cfg"/>.Type에 등록된 노드 타입의 인스턴스를 생성합니다. 실제 조회·생성 로직은
    /// <see cref="INodeRegistry.CreateInstance"/>(구현체: <c>NodeTypeRegistry</c>)에 위임합니다.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg) => _registry.CreateInstance(cfg);

    /// <summary>배포된 노드 목록입니다. Key는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>입니다(위 remarks 참고).</summary>
    public IReadOnlyDictionary<string, IFlowNode> Nodes => _nodes;

    /// <summary>
    /// (★ RT-01b, Full 모드만) <paramref name="flow"/>의 모든 노드를 <see cref="CreateInstance"/>로 먼저
    /// 전부 생성한 뒤, 그다음 전체 노드의 <c>OnStartAsync</c>를 <paramref name="flow"/>.Nodes 순서대로
    /// 호출합니다. 예외 격리·MissingNode 대체는 <c>RT-02a/b</c>에서, 변경분만 재시작하는 부분 재배포는
    /// <c>RT-03</c>에서 다룹니다 — 이 메서드는 아직 둘 다 하지 않습니다(노드 타입을 못 찾거나 기동이
    /// 실패하면 예외가 그대로 전파됩니다).
    /// </summary>
    public async Task DeployAsync(FlowDefinition flow, CancellationToken ct)
    {
        var created = new List<IFlowNode>(flow.Nodes.Count);
        foreach (var cfg in flow.Nodes)
        {
            var node = CreateInstance(cfg);
            _nodes[cfg.Id] = node;
            created.Add(node);
        }

        foreach (var node in created)
        {
            await node.OnStartAsync(BuildContext(node), ct);
        }
    }

    /// <summary>
    /// (★ RT-01b) <paramref name="node"/>에 전달할 <see cref="INodeContext"/>를 만듭니다. 02번 문서
    /// 3번 탭 카드6·2번 탭 카드8에 호출부만 있고 정식 선언이 없던 <c>BuildContext</c>를 이 Step에서
    /// 처음 구현 — 실제 <c>NodeContext</c>(<c>RT-09</c>)가 준비되기 전까지는 <see cref="NoOpNodeContext"/>를
    /// 반환하는 임시 자리표시자입니다.
    /// </summary>
    private INodeContext BuildContext(IFlowNode node) => new NoOpNodeContext();

    /// <summary>
    /// <c>RT-09</c>에서 실제 <c>NodeContext</c>가 만들어지기 전까지
    /// <see cref="BuildContext"/>가 반환하는 임시 무동작 구현입니다. <c>RouteAsync</c>는 아무 노드로도
    /// 전달하지 않고(<c>RT-04a</c>가 실제 라우팅을 구현), <c>SetStatus</c>는 아무것도 하지 않습니다
    /// (<c>RT-07</c> EventBus 연동 전까지).
    /// </summary>
    private sealed class NoOpNodeContext : INodeContext
    {
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) => Task.CompletedTask;

        public void SetStatus(string fill, string shape, string text) { }
    }
}
