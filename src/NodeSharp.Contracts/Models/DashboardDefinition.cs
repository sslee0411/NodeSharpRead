namespace NodeSharp.Contracts.Models;

/// <summary>
/// 운영자용 실시간 화면(웹 <c>/ui</c> + WPF 듀얼 렌더링)의 레이아웃 전체를 나타내는 최상위
/// 모델입니다. <c>dashboard.json</c>으로 저장되며, Node-RED Dashboard(<c>node-red-dashboard</c>)와
/// 동일한 Tab &gt; Group &gt; Widget 3단계 계층을 따릅니다. 현장 운영자가 태블릿·브라우저로
/// 보는 경우가 많아, 같은 위젯 정의를 웹과 WPF 양쪽에 렌더링합니다.
/// 설계 근거: 02번 문서 9번 탭 카드 11.
/// </summary>
/// <remarks>
/// 이 파일은 순수 데이터 계층만 정의합니다. <c>ui_*</c> 노드 구현·SignalR 스트리밍·WPF/웹
/// 렌더링은 각각 별도 Step에서 다룹니다.
/// </remarks>
/// <param name="Tabs">이 대시보드를 구성하는 탭 목록. 화면 상단(또는 사이드바)에 탭으로 나열됩니다.</param>
/// <example>
/// <code>
/// // 탭 1개("1호기 현황") 안에 그룹 2개(계측값 게이지, 제어 버튼)를 배치한 예
/// var measureGroup = new DashboardGroupDto(
///     Id: "group-1", Name: "압력/온도", Width: 6,
///     WidgetNodeIds: new List&lt;string&gt; { "ui-gauge-pressure", "ui-gauge-temp" });
///
/// var controlGroup = new DashboardGroupDto(
///     Id: "group-2", Name: "펌프 제어", Width: 3,
///     WidgetNodeIds: new List&lt;string&gt; { "ui-button-pump-start", "ui-button-pump-stop" });
///
/// var tab1 = new DashboardTabDto(
///     Id: "tab-1", Name: "1호기 현황",
///     Groups: new List&lt;DashboardGroupDto&gt; { measureGroup, controlGroup });
///
/// var dashboard = new DashboardDefinition(Tabs: new List&lt;DashboardTabDto&gt; { tab1 });
///
/// // 위젯 자체 데이터는 여전히 캔버스의 ui-gauge-pressure 노드가 msg.payload로 스트리밍한다(이 레코드는 레이아웃만 정의)
/// </code>
/// </example>
public sealed record DashboardDefinition(IReadOnlyList<DashboardTabDto> Tabs);

/// <summary>
/// <see cref="DashboardDefinition"/> 안의 탭 하나입니다. Node-RED Dashboard의 "Tab"에 대응합니다.
/// </summary>
/// <param name="Id">이 탭의 고유 식별자.</param>
/// <param name="Name">화면 상단(또는 사이드바)에 표시되는 탭 이름(예: "1호기 현황").</param>
/// <param name="Groups">이 탭 안에 배치된 그룹 목록.</param>
public sealed record DashboardTabDto(string Id, string Name, IReadOnlyList<DashboardGroupDto> Groups);

/// <summary>
/// <see cref="DashboardTabDto"/> 안의 그룹 하나입니다. Node-RED Dashboard의 "Group"(위젯을 묶는
/// 카드형 패널)에 대응하며, 실제 위젯 인스턴스는 담지 않고 <see cref="WidgetNodeIds"/>로 Flow
/// 캔버스의 <c>ui_*</c> 노드를 참조합니다 — 위젯 자체는 여전히 "노드"라서 캔버스에서 메시지를 주고받습니다.
/// </summary>
/// <param name="Id">이 그룹의 고유 식별자.</param>
/// <param name="Name">그룹 카드 상단에 표시되는 제목.</param>
/// <param name="Width">그룹 카드의 너비(Node-RED Dashboard의 6분할 그리드 단위와 동일한 개념).</param>
/// <param name="WidgetNodeIds">이 그룹에 배치된 <c>ui_*</c> 노드(<c>UiGaugeNode</c>/<c>UiButtonNode</c> 등)의 Id 목록. 위젯의 실제 동작은 캔버스에 배치된 그 노드가 담당합니다.</param>
public sealed record DashboardGroupDto(string Id, string Name, int Width, IReadOnlyList<string> WidgetNodeIds);
