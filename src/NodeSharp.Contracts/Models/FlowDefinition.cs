namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 플로우 정의
/// 역활 및 기능 : 캔버스의 Flow 탭 하나에 대응하는 최상위 모델
///
/// 캔버스의 Flow 탭 하나에 대응하는 최상위 모델입니다. <c>flows.json</c>은 이
/// <see cref="FlowDefinition"/>의 목록(사용자가 만든 Flow 탭 개수만큼)으로 구성됩니다.
/// 설계 근거: 02번 문서 2번 탭 카드 10(정식 선언).
/// </summary>
/// <remarks>
/// <see cref="Id"/>/<see cref="Name"/>은 캔버스 상단 플로우 탭 스트립(여러 <see cref="FlowDefinition"/>을
/// 탭으로 전환하며 편집하는 UI, <c>EC-05</c>에서 구현)의 데이터 기반입니다. 이 파일은 데이터
/// 모델만 정의하며, 실제 탭 전환 화면은 <c>EC-05</c>에서 구현합니다.
/// </remarks>
/// <param name="Id">이 Flow 탭의 고유 식별자. 캔버스 상단 탭 스트립에서 어떤 탭이 선택되어 있는지 구분하는 키입니다.</param>
/// <param name="Name">캔버스 상단 탭에 표시되는 이름(예: "1호기 라인", "공통 알림").</param>
/// <param name="Nodes">이 Flow 탭에 배치된 모든 노드의 설정 목록.</param>
/// <param name="Wires">이 Flow 탭 안의 노드들을 잇는 모든 연결선 목록.</param>
/// <param name="Disabled">이 Flow 탭 전체가 비활성화되어 있는지. <c>true</c>면 배포 시 이 탭에 속한 노드는 하나도 생성되지 않습니다(노드 단위 <see cref="NodeConfig.Disabled"/>와는 범위가 다릅니다).</param>
/// <param name="Groups">(EC-10) 이 Flow 탭 안의 캔버스 그룹(Group) 목록. <c>null</c>이면 그룹이 하나도 없는 것과 동일하게 취급합니다 — 순수 표시 전용 정보라 <c>FlowDeployer</c>/<c>FlowEngine</c>은 이 값을 읽지 않습니다.</param>
/// <example>
/// <code>
/// // Inject → Function → Alarm 3개 노드가 순서대로 연결된 Flow 탭 하나를 구성하는 예
/// var inject = new NodeConfig(Id: "n1", Type: "inject", Name: "1초 주기", FlowId: "flow-1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["interval"] = 1000 });
/// var func = new NodeConfig(Id: "n2", Type: "function", Name: "온도 변환", FlowId: "flow-1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["code"] = "return msg.payload * 1.8 + 32;" });
/// var alarm = new NodeConfig(Id: "n3", Type: "alarm-broadcast", Name: "알람 전파", FlowId: "flow-1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["level"] = "HH" });
///
/// var line1 = new FlowDefinition(
///     Id: "flow-1", Name: "1호기 라인",
///     Nodes: new List&lt;NodeConfig&gt; { inject, func, alarm },
///     Wires: new List&lt;Wire&gt;
///     {
///         new(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0),
///         new(SourceNodeId: "n2", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0),
///     });
///
/// // 비활성화된 두 번째 탭 — Disabled=true면 배포 시 n4/n5는 하나도 생성되지 않는다
/// var line2 = line1 with { Id = "flow-2", Name = "2호기 라인(점검 중)", Disabled = true };
///
/// // (EC-10) inject/func 2개 노드를 "전처리" 그룹으로 묶은 세 번째 탭 — Groups는 순수 표시 전용이라
/// // Runner의 FlowDeployer/FlowEngine은 이 값을 읽지 않는다
/// var line3 = line1 with
/// {
///     Id = "flow-3",
///     Name = "3호기 라인",
///     Groups = new List&lt;GroupDefinition&gt;
///     {
///         new(Id: "g1", Name: "전처리", MemberNodeIds: new List&lt;string&gt; { "n1", "n2" }),
///     },
/// };
/// </code>
/// </example>
public sealed record FlowDefinition(
    string Id,
    string Name,
    IReadOnlyList<NodeConfig> Nodes,
    IReadOnlyList<Wire> Wires,
    bool Disabled = false,
    IReadOnlyList<GroupDefinition>? Groups = null);
