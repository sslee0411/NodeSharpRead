using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// 플로우 실행 엔진. Editor(미리보기 실행)와 Runner(운영 실행) 양쪽이 공유하는 순수 로직입니다
/// (WPF 비의존, 1번 탭 카드2). 이 클래스는 <c>RT-01~11</c>에 걸쳐 증분으로 완성됩니다 — 지금까지는
/// <see cref="CreateInstance"/>(<c>RT-01a</c>), Full 모드 <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>(<c>RT-01b</c>),
/// <see cref="MissingNode"/> 대체(<c>RT-02a</c>), CreateInstance/OnStartAsync 두 단계 전체 예외 격리
/// (<c>RT-02b</c>), <see cref="DeployMode"/> 4종에 따른 부분 재배포(<c>RT-03</c>)만 있습니다. 메시지
/// 라우팅(<c>RouteAsync</c>, <c>RT-04a</c>) 등은 아직 없습니다("뼈대 우선, 확장" 원칙, 03번 Step맵 카드1).
/// 설계 근거: 02번 문서 2번 탭 카드 4·카드 9(정식 기준본)·카드 10(FlowDefinition/NodeConfig 정식 선언),
/// 3번 탭 카드 3(노드 생명주기 시퀀스)·카드 5(배포 모드 세분화)·카드 6(배포 예외 격리 — <c>BuildContext</c> 참조부).
/// </summary>
/// <remarks>
/// <see cref="_registry"/>가 실제 인스턴스 생성을 담당합니다(<c>NodeSharp.Registry.NodeTypeRegistry</c>,
/// <c>CT-06b</c>+<c>RT-01a</c>) — <see cref="FlowEngine"/> 자체는 타입 조회 방법을 모르고 <see cref="INodeRegistry"/>
/// 계약에만 의존합니다.
/// <para>
/// (★ RT-01b) <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>는 02번 문서 2번 탭 카드4 원본 스니펫처럼 <b>두 단계</b>로 나뉩니다 —
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
/// (★ RT-02b) <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>의 두 단계 모두 노드별로 예외를 격리합니다. 1단계(생성)에서는
/// <c>RT-02a</c>가 좁게 잡던 <see cref="InvalidOperationException"/>(등록되지 않은 타입) 대신 02번 문서
/// 2번 탭 카드4 원본과 동일하게 <b>모든 예외</b>를 잡아 <see cref="MissingNode"/>로 대체합니다(타입은
/// 찾았지만 생성자에서 예외를 던지는 경우 등도 포함). 2단계(기동)에서는 <c>OnStartAsync</c>가 예외를
/// 던지면(예: 잘못된 IP 주소) 그 노드만 <see cref="FailedNodeIds"/>에 기록하고 나머지 노드는 계속
/// 정상 기동합니다 — "설정 오류 하나가 전체 시스템을 멈추면 안 된다"는 원칙(3번 탭 카드6). 두 단계
/// 모두 <c>NodeErrorEvent</c> 발행은 <c>EventBus</c>(<c>RT-07</c>)가 아직 없어 범위 밖입니다.
/// </para>
/// <para>
/// (★ RT-03) <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>는 02번 문서 3번 탭
/// 카드5 <c>DeployMode</c> 4종(<c>Full/ModifiedFlows/ModifiedNodes/RestartFlows</c>)에 따라 재배포 범위를
/// 좁힙니다. 착수 중 발견한 공백과 그 처리:
/// <list type="bullet">
/// <item><b><c>DiffNodeConfigs</c> 정식 선언 없음</b> — 카드5 의사코드는 <c>DiffNodeConfigs(_currentFlow, newFlow)</c>를
/// 호출부만 보여줍니다. <c>NodeConfig.cs</c> remarks(CT 단계 정식 선언 시점에 이미 명시)가 "record 기본
/// <c>==</c>는 <see cref="NodeConfig.Properties"/> 딕셔너리를 참조 비교하므로 RT-03은 필드 단위로 비교해야
/// 한다"고 지시하므로, <see cref="NodeConfigsDiffer"/>로 Id를 제외한 전 필드(Type/Name/FlowId/
/// OutputDispatch/MaxConcurrency/CredentialRefId/Disabled/Properties 키-값)를 비교하도록 구현했습니다.</item>
/// <item><b><c>ChangedFlowIds</c> 정식 선언 없음</b> — 카드5 의사코드는 필드처럼 참조만 하고 계산 방법이
/// 없습니다. 직전 배포(<see cref="_currentFlow"/>)와 이번 <c>newFlow</c>를 노드 단위로 비교해 "추가/변경/
/// 삭제된 노드가 속한 <see cref="NodeConfig.FlowId"/> 집합"으로 정의했습니다 — <c>ModifiedFlows</c>가
/// <c>ModifiedNodes</c>보다 넓은 범위(같은 탭 안의 무변경 노드까지 함께 재시작)라는 카드5 설명과 일치하는
/// 가장 단순한 해석입니다.</item>
/// <item><b>삭제된 노드 처리</b> — 카드5 의사코드는 <c>newFlow</c>에 없어진 기존 노드를 다루지 않습니다.
/// 이번 Step에서는 재시작 대상 범위(<c>Full</c>: 전체, <c>ModifiedNodes</c>: 변경분, <c>ModifiedFlows</c>:
/// 변경된 탭 전체) 안에 있으면서 더 이상 <c>newFlow.Nodes</c>에 없는 기존 노드는 <c>OnCloseAsync</c> 호출 후
/// <see cref="Nodes"/>에서 제거합니다("정지 후 재시작" 원칙의 자연스러운 연장 — 재생성할 새 설정이 없으므로
/// 재생성 없이 제거만 함). <c>RestartFlows</c>는 "설정 변경 없이 재시작"이 전제이므로 diff를 계산하지 않고
/// 카드5 의사코드 그대로 <see cref="Nodes"/> 전체를 재시작 대상으로 삼습니다.</item>
/// <item><b>기존 2-인자 <see cref="DeployAsync(FlowDefinition, CancellationToken)"/> 유지</b> — 카드5는
/// <c>DeployAsync(FlowDefinition, DeployMode, CancellationToken)</c> 시그니처만 보여주지만, <c>RT-01b</c>부터
/// 있던 2-인자 오버로드를 제거하면 기존 <c>RT-01b/02a/02b</c> 테스트가 모두 깨집니다. 2-인자 오버로드는
/// <c>DeployMode.Full</c>로 위임하는 얇은 래퍼로 남겨 하위 호환을 유지합니다.</item>
/// </list>
/// 각 모드가 고르는 재시작 대상 집합은 <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>의
/// <c>switch</c>를 참고하십시오. 재시작 대상 노드는 (1) 기존 인스턴스가 있으면 <c>OnCloseAsync</c> 호출 →
/// (2) <c>newFlow.Nodes</c> 순서대로 <see cref="CreateInstance"/>(예외는 <c>RT-02b</c>와 동일하게 <see cref="MissingNode"/>로 흡수)
/// → (3) 새로 생성된 노드만 순서대로 <c>OnStartAsync</c>(실패는 <c>RT-02b</c>와 동일하게 <see cref="FailedNodeIds"/>에 기록,
/// <see cref="MissingNode"/>는 건너뜀) 순으로 처리됩니다. 재시작 대상이 아닌 기존 노드는 인스턴스를 그대로
/// 유지합니다(연결이 끊기지 않음 — 이 Step의 존재 이유).
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
///
/// // RT-03 — n1의 Name만 바꿔 ModifiedNodes로 재배포하면 n1만 재시작되고 n2는 손대지 않는다.
/// var changedCfg = cfg with { Name = "타이머(변경됨)" };
/// var flow2 = flow with { Nodes = new[] { changedCfg, badCfg } };
/// await engine.DeployAsync(flow2, DeployMode.ModifiedNodes, CancellationToken.None);
/// </code>
/// </example>
public sealed class FlowEngine
{
    private readonly INodeRegistry _registry;
    private readonly Dictionary<string, IFlowNode> _nodes = new();
    private readonly List<string> _failedNodes = new();

    /// <summary>
    /// (★ RT-03) 직전 <c>DeployAsync</c> 호출에 사용된 <see cref="FlowDefinition"/>입니다. 부분 재배포 모드
    /// (<see cref="DeployMode.ModifiedNodes"/>/<see cref="DeployMode.ModifiedFlows"/>)가 "무엇이 바뀌었는지"
    /// 판단할 비교 기준(baseline)으로 사용합니다. 최초 배포 전에는 <c>null</c>이며, 이 경우 모든 노드를
    /// "추가됨"으로 취급합니다(= Full과 동일하게 전체 생성).
    /// </summary>
    private FlowDefinition? _currentFlow;

    /// <summary>노드 타입 조회·인스턴스 생성을 위임할 레지스트리를 받아 엔진을 생성합니다.</summary>
    public FlowEngine(INodeRegistry registry) => _registry = registry;

    /// <summary>
    /// <paramref name="cfg"/>.Type에 등록된 노드 타입의 인스턴스를 생성합니다. 실제 조회·생성 로직은
    /// <see cref="INodeRegistry.CreateInstance"/>(구현체: <c>NodeTypeRegistry</c>)에 위임합니다.
    /// <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>는 이 메서드가 던지는 예외를 잡아 <see cref="MissingNode"/>로 대체하지만,
    /// 이 메서드를 직접 호출하면(<c>RT-01a</c> 당시와 동일하게) 예외가 그대로 전파됩니다.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg) => _registry.CreateInstance(cfg);

    /// <summary>배포된 노드 목록입니다. Key는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>입니다(위 remarks 참고).</summary>
    public IReadOnlyDictionary<string, IFlowNode> Nodes => _nodes;

    /// <summary>
    /// (★ RT-02b) <c>OnStartAsync</c> 단계에서 예외가 발생해 기동에 실패한 노드의 <see cref="NodeConfig.Id"/>
    /// 목록입니다. 7번 탭 헬스체크 엔드포인트가 참조할 예정(카드4 <c>FailedNodeIds</c> 원본과 동일 목적).
    /// (★ RT-03) 재배포마다 이번 배포에서 실제로 기동을 시도한 노드 기준으로 다시 계산됩니다 — 이전
    /// 배포에서 실패했던 노드가 이번 배포 대상에서 빠지면(다른 모드로 재시작 범위 밖) 더 이상 이 목록에
    /// 남지 않습니다(항상 "가장 최근 배포 결과"를 반영).
    /// </summary>
    public IReadOnlyList<string> FailedNodeIds => _failedNodes;

    /// <summary>
    /// (★ RT-03) 하위 호환용 2-인자 오버로드입니다. <c>RT-01b</c>부터 있던 기존 시그니처를 유지하기 위해
    /// <see cref="DeployMode.Full"/>로 <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>에
    /// 위임합니다 — 동작은 이전과 동일합니다(전체 정지 후 전체 재시작).
    /// </summary>
    public Task DeployAsync(FlowDefinition flow, CancellationToken ct) =>
        DeployAsync(flow, DeployMode.Full, ct);

    /// <summary>
    /// (★ RT-03) <paramref name="mode"/>에 따라 재배포 범위를 좁혀 적용합니다(02번 문서 3번 탭 카드5).
    /// 재시작 대상으로 뽑힌 노드만 (기존 인스턴스가 있으면) <c>OnCloseAsync</c> → <see cref="CreateInstance"/>
    /// (예외는 <see cref="MissingNode"/>로 흡수, <c>RT-02b</c>) → <c>OnStartAsync</c>(실패는 <see cref="FailedNodeIds"/>에
    /// 기록, <c>RT-02b</c>) 순으로 처리되고, 대상 밖 노드는 기존 인스턴스를 그대로 유지합니다(연결 유지).
    /// <paramref name="flow"/>.Nodes에서 사라진 기존 노드는(재시작 대상 범위 안이면) <c>OnCloseAsync</c> 후
    /// <see cref="Nodes"/>에서 제거됩니다. 모드별 재시작 대상 판단 기준은 클래스 remarks(★ RT-03)를 참고하십시오.
    /// </summary>
    public async Task DeployAsync(FlowDefinition flow, DeployMode mode, CancellationToken ct)
    {
        var oldById = _currentFlow?.Nodes.ToDictionary(n => n.Id) ?? new Dictionary<string, NodeConfig>();
        var newById = flow.Nodes.ToDictionary(n => n.Id);

        // ★ RT-03: 변경분(추가/필드변경)과 삭제분은 모드에 상관없이 항상 같은 기준으로 먼저 계산한다 —
        //   ModifiedNodes는 이 변경분 자체를, ModifiedFlows는 이 변경분이 속한 FlowId 전체를 재시작 대상으로 삼는다.
        var changedIds = new HashSet<string>(
            newById.Where(kv => !oldById.TryGetValue(kv.Key, out var old) || NodeConfigsDiffer(old, kv.Value))
                   .Select(kv => kv.Key));
        var removedIds = new HashSet<string>(oldById.Keys.Except(newById.Keys));

        HashSet<string> restartIds;   // newById 기준 — 새로 생성/재시작할 노드 Id
        HashSet<string> closeOnlyIds; // oldById 기준이지만 newById에는 없음 — 재생성 없이 닫고 제거만 할 노드 Id

        switch (mode)
        {
            case DeployMode.ModifiedNodes:
                // 이전 설정과 필드 단위로 비교해 실제로 변경된 노드만(가장 안전, 카드5)
                restartIds = changedIds;
                closeOnlyIds = removedIds;
                break;

            case DeployMode.ModifiedFlows:
                // 변경/삭제된 노드가 속한 FlowId(탭) 전체를 재시작 대상으로 넓힌다(★ RT-03 ChangedFlowIds 정의)
                var changedFlowIds = new HashSet<string>(
                    changedIds.Select(id => newById[id].FlowId)
                        .Concat(removedIds.Select(id => oldById[id].FlowId)));
                restartIds = new HashSet<string>(newById.Where(kv => changedFlowIds.Contains(kv.Value.FlowId)).Select(kv => kv.Key));
                closeOnlyIds = new HashSet<string>(removedIds.Where(id => changedFlowIds.Contains(oldById[id].FlowId)));
                break;

            case DeployMode.Full:
            case DeployMode.RestartFlows:
            default:
                // 카드5 의사코드 그대로 — 두 모드 모두 전체 재시작(Full은 설정도 새로 반영, RestartFlows는
                // "설정 변경 없이"가 전제이므로 newFlow==currentFlow 상황에서 호출되는 것을 기대함).
                restartIds = new HashSet<string>(newById.Keys);
                closeOnlyIds = removedIds;
                break;
        }

        // 1단계: 재시작 대상(기존 인스턴스가 있는 것만) + 삭제 대상을 먼저 닫는다 — 카드5 의사코드의
        //   "foreach (var node in toRestart) await node.OnCloseAsync(...)"에 삭제분 처리를 더한 것.
        foreach (var id in restartIds.Concat(closeOnlyIds))
        {
            if (_nodes.TryGetValue(id, out var existing))
            {
                try
                {
                    await existing.OnCloseAsync(BuildContext(existing));
                }
                catch (Exception)
                {
                    // ★ RT-03: 종료 단계 예외도 RT-02b와 동일한 원칙으로 흡수 — 노드 하나의 종료 실패가
                    //   재배포 전체를 막아서는 안 된다(연결이 이미 끊겨 있는 등 종료 자체가 실패할 수 있음).
                }
            }
        }

        foreach (var id in closeOnlyIds)
        {
            _nodes.Remove(id);
        }

        _failedNodes.Clear();   // ★ RT-03: 항상 "이번 배포"의 기동 실패만 반영(이전 배포의 실패 기록이 누적되지 않음)

        // 2단계: 재시작 대상만 newFlow.Nodes 순서대로 재생성(RT-02b와 동일한 예외 격리)
        var created = new List<(string CfgId, IFlowNode Node)>();
        foreach (var cfg in flow.Nodes)
        {
            if (!restartIds.Contains(cfg.Id)) continue;

            IFlowNode node;
            try
            {
                node = CreateInstance(cfg);
            }
            catch (Exception)
            {
                node = new MissingNode(cfg.Id, cfg.Type);
            }

            _nodes[cfg.Id] = node;
            created.Add((cfg.Id, node));
        }

        // 3단계: 새로 생성된 노드만 순서대로 기동(RT-02b와 동일한 예외 격리) — 재시작 대상이 아니었던
        //   기존 노드는 이 루프에 아예 들어오지 않으므로 OnStartAsync가 다시 호출되지 않는다(연결 유지).
        foreach (var (cfgId, node) in created)
        {
            if (node is MissingNode) continue;   // ★ RT-02a: 자리표시자는 OnStartAsync 자체가 없음

            try
            {
                await node.OnStartAsync(BuildContext(node), ct);
            }
            catch (Exception)
            {
                _failedNodes.Add(cfgId);
            }
        }

        _currentFlow = flow;   // ★ RT-03: 다음 배포의 diff 기준선을 이번 배포로 갱신
    }

    /// <summary>
    /// (★ RT-03) <paramref name="a"/>와 <paramref name="b"/>가 "내용상 같은 설정"인지 필드 단위로 비교합니다.
    /// <c>NodeConfig.cs</c> remarks가 명시하는 대로 record 기본 <c>==</c>에 의존하지 않습니다 — <see cref="NodeConfig.Properties"/>는
    /// 딕셔너리 참조 비교가 아니라 키/값 내용 비교로 판정합니다(<see cref="NodeConfig.Id"/>는 이미 같은 Id끼리
    /// 비교하는 호출부 계약이라 비교 대상에서 제외).
    /// </summary>
    private static bool NodeConfigsDiffer(NodeConfig a, NodeConfig b)
    {
        if (a.Type != b.Type || a.Name != b.Name || a.FlowId != b.FlowId ||
            a.OutputDispatch != b.OutputDispatch || a.MaxConcurrency != b.MaxConcurrency ||
            a.CredentialRefId != b.CredentialRefId || a.Disabled != b.Disabled)
        {
            return true;
        }

        if (a.Properties.Count != b.Properties.Count) return true;

        foreach (var (key, value) in a.Properties)
        {
            if (!b.Properties.TryGetValue(key, out var otherValue)) return true;
            if (!Equals(value, otherValue)) return true;
        }

        return false;
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
