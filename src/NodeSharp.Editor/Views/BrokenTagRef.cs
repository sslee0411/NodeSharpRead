namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 끊어진 TagRef 참조
/// 역활 및 기능 : 캔버스 노드의 TagRef 속성 필드가 가리키는 TagId가 현재 구조 설정 트리에 존재하지
/// 않을 때, 그 위반 사실 하나를 담는 값
///
/// (ED-D05) <see cref="FlowCanvasView.FindBrokenTagRefs"/>가 이 값의 목록을 반환하고,
/// <c>MainWindow.OnSaveFlowClick</c>이 저장(=LK-01 자동 재배포 트리거) 직전에 호출해 사용자에게
/// 경고합니다(03번 Step맵 ED-D05 완료 기준 "배포 전 검사에 넣으면 찾아내 배포를 막거나 경고").
/// </summary>
/// <param name="NodeId">문제가 있는 캔버스 노드의 <see cref="NodeSharp.Contracts.Models.NodeConfig.Id"/>.</param>
/// <param name="NodeName">사용자에게 보여줄 그 노드의 이름(빈 문자열이면 타입명으로 대체해 표시).</param>
/// <param name="FieldKey">TagRef 타입인 <see cref="NodeSharp.Contracts.Models.PropertyField.Key"/>(예: "tagId").</param>
/// <param name="MissingTagId">구조 설정 트리에서 더 이상 찾을 수 없는(삭제되었거나 애초에 없던) 태그 Id 값.</param>
public sealed record BrokenTagRef(string NodeId, string NodeName, string FieldKey, string MissingTagId);
