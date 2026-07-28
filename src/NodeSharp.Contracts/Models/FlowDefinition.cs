namespace NodeSharp.Contracts.Models;

/// <summary>
/// 캔버스의 Flow 탭 하나에 대응하는 최상위 모델입니다. <c>flows.json</c>은 이
/// <see cref="FlowDefinition"/>의 목록(사용자가 만든 Flow 탭 개수만큼)으로 구성됩니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 2번 탭 카드 10 "NodeConfig / FlowDefinition 완전 정의" —
/// 이전에는 <c>FlowEngine.DeployAsync(FlowDefinition, ...)</c>, <c>SubflowDefinition.InnerFlow</c>
/// 등 여러 탭에서 타입으로만 언급되고 한 번도 정식 선언되지 않았던 것을 완성한 최종본입니다.
/// </para>
/// <para>
/// <b>다중 Flow 탭 지원의 데이터 기반</b>: <see cref="Id"/>/<see cref="Name"/>은 v1.12에서
/// 확정된 "캔버스 상단 플로우 탭 스트립"(여러 개의 <see cref="FlowDefinition"/>을 탭으로 전환하며
/// 편집하는 UI, Step <c>EC-05</c>에서 구현)의 데이터 기반입니다. 이 Step(<c>CT-02c</c>)에서는
/// 데이터 모델만 완성하고, 실제 탭 전환 화면은 <c>EC-05</c>에서 구현합니다.
/// </para>
/// </remarks>
/// <param name="Id">이 Flow 탭의 고유 식별자. 캔버스 상단 탭 스트립에서 어떤 탭이 선택되어 있는지 구분하는 키로 쓰입니다.</param>
/// <param name="Name">캔버스 상단 탭에 표시되는 이름(예: "1호기 라인", "공통 알림").</param>
/// <param name="Nodes">이 Flow 탭에 배치된 모든 노드의 설정 목록(<see cref="NodeConfig"/>).</param>
/// <param name="Wires">이 Flow 탭 안의 노드들을 잇는 모든 연결선 목록(<see cref="Models.Wire"/>).</param>
/// <param name="Disabled">
/// 이 Flow 탭 전체가 비활성화되어 있는지(Node-RED Flow 탭의 <c>disabled</c> 속성과 동일,
/// 9번 탭 Enable-Disable). <c>true</c>면 배포 시 이 탭에 속한 노드는 하나도 생성되지 않습니다
/// — <see cref="NodeConfig.Disabled"/>(노드 하나만 끄기)와는 범위가 다릅니다.
/// </param>
/// <example>
/// <code>
/// // 서로 다른 두 Flow 탭 — 탭 전환 UI(EC-05)는 Id로 선택 상태를 추적하고 Name을 화면에 표시한다
/// var line1 = new FlowDefinition(
///     Id: "flow-1", Name: "1호기 라인",
///     Nodes: new List&lt;NodeConfig&gt;(), Wires: new List&lt;Wire&gt;());
///
/// var line2 = new FlowDefinition(
///     Id: "flow-2", Name: "2호기 라인",
///     Nodes: new List&lt;NodeConfig&gt;(), Wires: new List&lt;Wire&gt;());
///
/// // 탭 전환 UI는 이렇게 서로 다른 Id/Name으로 두 탭을 구분한다
/// bool sameTab = line1.Id == line2.Id; // false
/// </code>
/// </example>
public sealed record FlowDefinition(
    string Id,
    string Name,
    IReadOnlyList<NodeConfig> Nodes,
    IReadOnlyList<Wire> Wires,
    bool Disabled = false);
