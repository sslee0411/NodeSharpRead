namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 노드 검색 결과
/// 역활 및 기능 : Explorer 패널 검색 결과 목록의 항목 하나(소속 Flow 탭 + 노드 식별 정보)를 담는 값
///
/// (EC-12) <see cref="FlowCanvasView.SearchNodes"/>가 돌려주는 값 타입입니다. 화면(Explorer 패널)은
/// 이 값만으로 결과 목록을 그리고, 항목을 클릭하면 <see cref="FlowId"/>/<see cref="NodeId"/>를 그대로
/// <see cref="FlowCanvasView.NavigateToNode"/>에 넘겨 해당 Flow 탭으로 전환 + 노드 선택(하이라이트)을
/// 트리거합니다 — <see cref="Views.InformationPanelView"/>가 <see cref="Contracts.Models.NodeConfig"/>를
/// 값으로만 전달받는 것과 같은 원칙으로, Explorer 패널도 <see cref="FlowCanvasView"/>를 직접 참조하지
/// 않습니다.
/// </summary>
/// <param name="FlowId">이 노드가 속한 Flow 탭의 <c>FlowDefinition.Id</c>(= <c>NodeConfig.FlowId</c>).</param>
/// <param name="FlowName">사용자에게 보여줄 Flow 탭 이름(<see cref="FlowTabInfo.Name"/>).</param>
/// <param name="NodeId">노드의 고유 식별자(<c>NodeConfig.Id</c>) — <see cref="FlowCanvasView.NavigateToNode"/> 호출 시 그대로 전달합니다.</param>
/// <param name="NodeName">노드의 표시 이름(<c>NodeConfig.Name</c>) — 검색어와 일치했을 수 있는 대상 중 하나입니다.</param>
/// <param name="NodeType">노드 타입 이름(<c>NodeConfig.Type</c>) — 결과 목록에서 노드를 구분하기 위해 함께 표시합니다.</param>
public sealed record NodeSearchResult(string FlowId, string FlowName, string NodeId, string NodeName, string NodeType);
