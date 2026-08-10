namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 그룹 정의
/// 역활 및 기능 : 캔버스에서 여러 노드를 하나의 사각형 박스로 묶는 시각 전용 요소
///
/// 캔버스에서 여러 노드를 사각형 박스로 묶어 이름을 표시하고 접기(Collapse)할 수 있게 하는
/// Node-RED 1.1+ 표준 기능("Group")입니다. <see cref="NodeSharp.Contracts.Interfaces.IFlowNode"/>를
/// 구현하지 않고 <c>FlowEngine</c>이 배포하는 대상도 아닙니다(<c>NR-17b</c>의 Junction과 같은
/// 성격 — 캔버스 표시·flows.json 저장 전용, Runner의 실행 그래프에는 나타나지 않음).
/// 설계 근거: 03번 Step맵 <c>EC-10</c> desc(<c>CT-02b</c>와 같은 위치에 신설).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>탭 소속</b>: 이 레코드 자체에는 <see cref="NodeConfig.FlowId"/>에 대응하는 필드가 없습니다
/// — <see cref="MemberNodeIds"/>가 가리키는 노드들의 <see cref="NodeConfig.FlowId"/>로 소속 탭을
/// 간접적으로 판단합니다(그룹의 모든 멤버는 항상 같은 탭에 속한다는 전제, <c>Wire</c>가 탭 구분 없이
/// Id만으로 노드를 참조하는 것과 같은 설계 원칙).</item>
/// <item><b>배포 대상 아님</b>: <c>FlowDeployer</c>/<c>FlowEngine</c>은 <see cref="FlowDefinition.Groups"/>를
/// 전혀 읽지 않습니다 — 그룹은 Editor 캔버스의 표시 전용 개념이라 Runner 쪽 실행에는 아무 영향을
/// 주지 않습니다.</item>
/// </list>
/// </remarks>
/// <param name="Id">이 그룹의 고유 식별자(예: "g1"). 탭 안에서 유일하면 충분합니다(노드 Id처럼 전역 유일할 필요는 없음).</param>
/// <param name="Name">캔버스에 표시되는 그룹 이름(예: "온도 감시 로직").</param>
/// <param name="MemberNodeIds">이 그룹에 속한 노드들의 <see cref="NodeConfig.Id"/> 목록. 항상 같은 Flow 탭에 속한 노드들이어야 합니다.</param>
/// <param name="Collapsed">이 그룹이 접혀 있는지. <c>true</c>면 캔버스에서 소속 노드 카드 대신 박스 하나로 축약 표시됩니다.</param>
/// <param name="Color">그룹 박스 테두리 색(예: "#3B82F6"). <c>null</c>이면 테마의 기본 강조색(AccentBrush)을 사용합니다 — 색상 선택 UI는 이후 Step에서 추가될 수 있습니다.</param>
/// <example>
/// <code>
/// // 1) Inject/Function/Alarm 3개 노드를 "고온 감시" 그룹으로 묶기(펼친 상태)
/// var group = new GroupDefinition(
///     Id: "g1", Name: "고온 감시",
///     MemberNodeIds: new List&lt;string&gt; { "n1", "n2", "n3" },
///     Collapsed: false);
///
/// // 2) 캔버스에서 접기 버튼을 누르면 같은 그룹을 Collapsed=true로 교체(레코드라 새 인스턴스로 갱신)
/// var collapsed = group with { Collapsed = true };
///
/// // 3) 색상을 지정한 그룹 — 색상 선택 UI가 아직 없어도 모델 자체는 이미 이 값을 저장할 수 있음
/// var colored = group with { Color = "#3B82F6" };
/// </code>
/// </example>
public sealed record GroupDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> MemberNodeIds,
    bool Collapsed = false,
    string? Color = null);
