using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// <c>FlowEngine</c>이 <see cref="NodeConfig.Type"/> 문자열로부터 실제 <see cref="IFlowNode"/> 인스턴스를
/// 만들 때 의존하는 계약입니다. 구현체는 <c>NodeSharp.Registry.NodeTypeRegistry</c>(<c>CT-06b</c>)이며,
/// <c>PluginLoader</c>(<c>CT-06a</c>)로 로드한 플러그인 dll에서 수집한 타입 목록 중 <see cref="NodeConfig.Type"/>과
/// 일치하는 타입을 찾아 인스턴스화합니다. <c>FlowEngine</c>이 이 인터페이스를 직접 참조하는 이유는
/// <c>NodeSharp.Runtime</c>이 <c>NodeSharp.Registry</c>의 구체 클래스를 직접 몰라도 되게 하기 위함이
/// 아니라(실제로는 Runtime → Registry ProjectReference가 이미 있음, 1번 탭 카드2), 1번 탭 폴더 구조가
/// 이 계약을 애초에 <c>Contracts/Interfaces</c> 소속으로 명시하고 있고(<c>IFlowNode</c>와 나란히 두어
/// "노드를 다루는 최소 계약" 묶음을 한곳에 유지하기 위함)이기 때문입니다.
/// 설계 근거: 02번 문서 2번 탭 카드 4(<c>FlowEngine._registry.CreateInstance(cfg)</c>)·카드 9(<c>FlowEngine.Registry</c>
/// 프로퍼티), 3번 탭 카드 3(노드 생명주기 시퀀스 — <c>Eng-&gt;&gt;Reg: CreateInstance(NodeConfig)</c>).
/// </summary>
/// <remarks>
/// <b>RT-01a 범위 한정</b>: 이 Step은 "타입 이름으로 인스턴스를 만들 수 있는가"만 다룬다.
/// <see cref="Type"/>을 찾아 <c>Activator.CreateInstance</c>로 생성하는 방식이라, 각 노드 구현체는
/// 공개 매개변수 없는 생성자를 가져야 한다(<c>LssLibNodeAdapterBase</c>, 11번 탭 카드2가 이미 이
/// 관례를 따름). 생성된 인스턴스의 <see cref="IFlowNode.Id"/>를 <see cref="NodeConfig.Id"/>와 동기화하는
/// 정식 메커니즘(<c>INodeTypeDescriptor.Factory</c> 델리게이트 기반)은 <c>RG-01</c>에서 다룰 예정 — 지금은
/// 다루지 않는다(2번 탭 카드1 <c>IFlowNode</c> 원본 주석의 "인스턴스 생성 방법은 정의하지 않는다" 참고).
/// </remarks>
/// <example>
/// <code>
/// INodeRegistry registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
/// registry.TryRegister(new PluginManifest("inject", "1.0.0", "1.0.0"), typeof(InjectNode));
///
/// var cfg = new NodeConfig("n1", "inject", "타이머", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = registry.CreateInstance(cfg);   // typeof(InjectNode) 인스턴스 반환
///
/// // 등록되지 않은 타입 — 예외로 명확히 구분(FlowEngine.DeployAsync, RT-01b가 이 예외를 잡아 MissingNode로 대체 예정)
/// var badCfg = cfg with { Type = "no-such-type" };
/// Assert.Throws&lt;InvalidOperationException&gt;(() =&gt; registry.CreateInstance(badCfg));
/// </code>
/// </example>
public interface INodeRegistry
{
    /// <summary>
    /// <paramref name="cfg"/>.<see cref="NodeConfig.Type"/>에 해당하는 노드 타입을 찾아 인스턴스를
    /// 생성합니다. 등록된 타입이 없으면 <see cref="InvalidOperationException"/>을 던집니다.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    IFlowNode CreateInstance(NodeConfig cfg);
}
