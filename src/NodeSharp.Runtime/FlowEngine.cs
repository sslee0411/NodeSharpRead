using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// 플로우 실행 엔진. Editor(미리보기 실행)와 Runner(운영 실행) 양쪽이 공유하는 순수 로직입니다
/// (WPF 비의존, 1번 탭 카드2). 이 클래스는 <c>RT-01~11</c>에 걸쳐 증분으로 완성됩니다 — 지금까지는
/// <see cref="CreateInstance"/>(<c>RT-01a</c>), Full 모드 <see cref="DeployAsync"/>(<c>RT-01b</c>),
/// <see cref="MissingNode"/> 대체(<c>RT-02a</c>), CreateInstance/OnStartAsync 두 단계 전체 예외 격리
/// (<c>RT-02b</c>)만 있습니다. 부분 재배포(<c>RT-03</c>), 메시지 라우팅(<c>RouteAsync</c>, <c>RT-04a</c>)
/// 등은 아직 없습니다("뼈대 우선, 확장" 원칙, 03번 Step맵 카드1).
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
/// 임시 자리표시자이며, <see cref="MissingNode"/>와 동일한 "타입 시스템을 만족시키는 최소 스텁" 성격입니다.
/// </para>
/// <para>
/// ★ MissingNode 한줄 요약(★ RT-02a): <see cref="MissingNode"/>는 <b>"노드 타입을 찾을 수 없을 때"만</b>
/// 쓰는 자리표시자입니다 — 2단계(기동)에서는 <see cref="MissingNode"/>를 만나면 <c>OnStartAsync</c>
/// 호출 자체를 건너뜁니다(자리표시자는 "기동" 개념이 없음).
/// </para>
/// <para>
/// (★ RT-02b) <see cref="DeployAsync"/>의 두 단계 모두 노드별로 예외를 격리합니다. 1단계(생성)에서는
/// <c>RT-02a</c>가 좁게 잡던 <see cref="InvalidOperationException"/>(등록되지 않은 타입) 대신 02번 문서
/// 2번 탭 카드4 원본과 동일하게 <b>모든 예외</b>를 잡아 <see cref="MissingNode"/>로 대체합니다(타입은
/// 찾았지만 생성자에서 예외를 던지는 경우 등도 포함). 2단계(기동)에서는 <c>OnStartAsync</c>가 예외를
/// 던지면(예: 잘못된 IP 주소) 그 노드만 <see cref="FailedNodeIds"/>에 기록하고 나머지 노드는 계속
/// 정상 기동합니다 — "설정 오류 하나가 전체 시스템을 멈추면 안 된다"는 원칙(3번 탭 카드6). 두 단계
/// 모두 <c>NodeErrorEvent</c> 발행은 <c>EventBus</c>(<c>RT-07</c>)가 아직 없어 범위 밖입니다.
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
/// // RT-01b/RT-02a/RT-02b — Full 모드 배포. 등록되지 않은 타입이 섞여 있어도, 기동 중 예외가 나도
/// // 예외 없이 완료된다.
/// var badCfg = new NodeConfig("n2", "no-such-type", "삭제된 플러그인", "f1", new Dictionary&lt;string, object?&gt;());
/// var flow = new FlowDefinition("f1", "테스트", Nodes: new[] { cfg, badCfg }, Wires: Array.Empty&lt;Wire&gt;());
/// await engine.DeployAsync(flow, CancellationToken.None);
/// IFlowNode deployed = engine.Nodes["n1"];         // typeof(InjectNode) 인스턴스
/// IFlowNode missing = engine.Nodes["n2"];           // MissingNode 인스턴스 — 배포는 계속 성공
/// IReadOnlyList&lt;string&gt; failed = engine.FailedNodeIds;   // OnStartAsync 실패 노드 Id 목록
/// </code>
/// </example>
public sealed class FlowEngine
{
    private readonly INodeRegistry _registry;
    private readonly Dictionary<string, IFlowNode> _nodes = new();
    private readonly List<string> _failedNodes = new();

    /// <summary>노드 타입 조회·인스턴스 생성을 위임할 레지스트리를 받아 엔진을 생성합니다.</summary>
    public FlowEngine(INodeRegistry registry) => _registry = registry;

    /// <summary>
    /// <paramref name="cfg"/>.Type에 등록된 노드 타입의 인스턴스를 생성합니다. 실제 조회·생성 로직은
    /// <see cref="INodeRegistry.CreateInstance"/>(구현체: <c>NodeTypeRegistry</c>)에 위임합니다.
    /// <see cref="DeployAsync"/>는 이 메서드가 던지는 예외를 잡아 <see cref="MissingNode"/>로 대체하지만,
    /// 이 메서드를 직접 호출하면(<c>RT-01a</c> 당시와 동일하게) 예외가 그대로 전파됩니다.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg) => _registry.CreateInstance(cfg);

    /// <summary>배포된 노드 목록입니다. Key는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>입니다(위 remarks 참고).</summary>
    public IReadOnlyDictionary<string, IFlowNode> Nodes => _nodes;

    /// <summary>
    /// (★ RT-02b) <c>OnStartAsync</c> 단계에서 예외가 발생해 기동에 실패한 노드의 <see cref="NodeConfig.Id"/>
    /// 목록입니다. 7번 탭 헬스체크 엔드포인트가 참조할 예정(카드4 <c>FailedNodeIds</c> 원본과 동일 목적).
    /// </summary>
    public IReadOnlyList<string> FailedNodeIds => _failedNodes;

    /// <summary>
    /// (Full 모드만) <paramref name="flow"/>의 모든 노드를 <see cref="CreateInstance"/>로 먼저 전부 생성한
    /// 뒤(예외가 나면 <see cref="MissingNode"/>로 대체, <c>RT-02a/b</c>), 그다음 전체 노드의
    /// <c>OnStartAsync</c>를 <paramref name="flow"/>.Nodes 순서대로 호출합니다(<see cref="MissingNode"/>는
    /// 건너뜀, 개별 노드의 <c>OnStartAsync</c> 실패는 <see cref="FailedNodeIds"/>에 기록하고 계속 진행,
    /// <c>RT-02b</c>). 변경분만 재시작하는 부분 재배포는 <c>RT-03</c>에서 다룹니다.
    /// </summary>
    public async Task DeployAsync(FlowDefinition flow, CancellationToken ct)
    {
        // ★ RT-02b: NodeConfig.Id를 노드와 함께 들고 다닌다 — IFlowNode.Id는 RT-01a에서 동기화를
        //   RG-01로 미뤄둔 값이라 신뢰할 수 없으므로, FailedNodeIds에는 항상 안정적인 cfg.Id를 기록한다.
        var created = new List<(string CfgId, IFlowNode Node)>(flow.Nodes.Count);
        foreach (var cfg in flow.Nodes)
        {
            IFlowNode node;
            try
            {
                node = CreateInstance(cfg);
            }
            catch (Exception)
            {
                // ★ RT-02b: RT-02a는 InvalidOperationException(등록되지 않은 타입)만 잡았으나,
                //   02번 문서 2번 탭 카드4 원본처럼 생성 단계의 모든 예외를 자리표시자로 흡수한다
                //   ("설정 오류 하나가 전체 시스템을 멈추면 안 된다").
                node = new MissingNode(cfg.Id, cfg.Type);
            }

            _nodes[cfg.Id] = node;
            created.Add((cfg.Id, node));
        }

        foreach (var (cfgId, node) in created)
        {
            if (node is MissingNode) continue;   // ★ RT-02a: 자리표시자는 OnStartAsync 자체가 없음(MissingNode.cs 참고)

            try
            {
                await node.OnStartAsync(BuildContext(node), ct);
            }
            catch (Exception)
            {
                // ★ RT-02b: 이 노드만 기동 실패로 기록하고 나머지 노드는 계속 정상 기동(3번 탭 카드6)
                _failedNodes.Add(cfgId);
            }
        }
    }

    /// <summary>
    /// <paramref name="node"/>에 전달할 <see cref="INodeContext"/>를 만듭니다. 02번 문서 3번 탭 카드6·
    /// 2번 탭 카드8에 호출부만 있고 정식 선언이 없던 <c>BuildContext</c>를 <c>RT-01b</c>에서 처음 구현
    /// — 실제 <c>NodeContext</c>(<c>RT-09</c>)가 준비되기 전까지는 <see cref="NoOpNodeContext"/>를
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
