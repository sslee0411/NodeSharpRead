namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Flow 탭 정보
/// 역활 및 기능 : 캔버스 상단 탭 스트립에 표시되는 Flow 탭 하나의 식별자·이름을 담는 값
///
/// (EC-05, ★ 사용자 요청) 캔버스 상단 탭 스트립(12번 탭 카드2 mock-toolbar "1호기 라인/2호기
/// 라인/공통 알림/＋")이 어떤 탭이 몇 개 있고 각각 이름이 무엇인지 화면에 그리는 데만 쓰는 가벼운
/// Editor 전용 값입니다. 저장되는 실제 데이터(<see cref="NodeSharp.Contracts.Models.FlowDefinition"/>)와
/// 달리 Nodes/Wires를 직접 담지 않습니다 — 이 뷰는 모든 탭의 노드를 <c>_nodeConfigs</c>/<c>_wires</c>
/// 하나의 목록에 함께 보관하고 각 <c>NodeConfig.FlowId</c>로 소속 탭을 구분하므로, 탭 자체는 Id/Name만
/// 있으면 충분합니다. 저장 시점(<c>SaveFlowAsync</c>)에 이 값과 <c>NodeConfig.FlowId</c>를 조합해
/// 탭별로 실제 <see cref="NodeSharp.Contracts.Models.FlowDefinition"/>을 만듭니다.
/// </summary>
public sealed record FlowTabInfo(string Id, string Name);
