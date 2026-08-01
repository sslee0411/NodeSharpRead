using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// 플로우 실행 엔진. Editor(미리보기 실행)와 Runner(운영 실행) 양쪽이 공유하는 순수 로직입니다
/// (WPF 비의존, 1번 탭 카드2). 이 클래스는 <c>RT-01~11</c>에 걸쳐 증분으로 완성됩니다 — 이 Step
/// (<c>RT-01a</c>)에서는 <see cref="CreateInstance"/>만 다루고, 배포(<c>DeployAsync</c>, <c>RT-01b</c>)·
/// 메시지 라우팅(<c>RouteAsync</c>, <c>RT-04a</c>) 등은 아직 없습니다("뼈대 우선, 확장" 원칙, 03번
/// Step맵 카드1).
/// 설계 근거: 02번 문서 2번 탭 카드 4·카드 9(정식 기준본), 3번 탭 카드 3(노드 생명주기 시퀀스).
/// </summary>
/// <remarks>
/// <see cref="_registry"/>가 실제 인스턴스 생성을 담당합니다(<c>NodeSharp.Registry.NodeTypeRegistry</c>,
/// <c>CT-06b</c>+<c>RT-01a</c>) — <see cref="FlowEngine"/> 자체는 타입 조회 방법을 모르고 <see cref="INodeRegistry"/>
/// 계약에만 의존합니다. <see cref="CreateInstance"/>가 이 클래스에 있는 이유는 <c>RT-01b</c>의
/// <c>DeployAsync</c>가 노드별로 이 메서드를 호출해 배포 루프를 구성할 예정이기 때문입니다(2번 탭 카드4
/// <c>DeployAsync</c> 원본 스니펫 — <c>_registry.CreateInstance(cfg)</c>가 이 안에서 호출됨).
/// </remarks>
/// <example>
/// <code>
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
/// registry.TryRegister(new PluginManifest("inject", "1.0.0", "1.0.0"), typeof(InjectNode));
///
/// var engine = new FlowEngine(registry);
/// var cfg = new NodeConfig("n1", "inject", "타이머", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = engine.CreateInstance(cfg);   // typeof(InjectNode) 인스턴스 반환
///
/// // 등록되지 않은 타입 — 예외로 명확히 구분(완료 기준)
/// var badCfg = cfg with { Type = "no-such-type" };
/// Assert.Throws&lt;InvalidOperationException&gt;(() =&gt; engine.CreateInstance(badCfg));
/// </code>
/// </example>
public sealed class FlowEngine
{
    private readonly INodeRegistry _registry;

    /// <summary>노드 타입 조회·인스턴스 생성을 위임할 레지스트리를 받아 엔진을 생성합니다.</summary>
    public FlowEngine(INodeRegistry registry) => _registry = registry;

    /// <summary>
    /// <paramref name="cfg"/>.Type에 등록된 노드 타입의 인스턴스를 생성합니다. 실제 조회·생성 로직은
    /// <see cref="INodeRegistry.CreateInstance"/>(구현체: <c>NodeTypeRegistry</c>)에 위임합니다.
    /// 배포(<c>OnStartAsync</c> 호출, Wire 연결 등)는 이 메서드 범위 밖입니다 — <c>RT-01b</c> 참고.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg) => _registry.CreateInstance(cfg);
}
