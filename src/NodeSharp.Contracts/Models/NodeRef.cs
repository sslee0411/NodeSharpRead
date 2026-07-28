namespace NodeSharp.Contracts.Models;

/// <summary>
/// 특정 Flow의 특정 노드를 가리키는 최소 참조입니다. 태그·시퀀스 등을 "누가 사용하고 있는지"
/// 역방향으로 조회하는 결과 항목으로 쓰입니다(예: 삭제 가능 여부 판단, 캔버스 하이라이트 이동).
/// <c>IStructureService.FindNodesByTagRef</c>와 <c>IFlowNodeIndex.FindNodesBySequenceId</c>가
/// 공통으로 이 타입을 반환합니다.
/// 설계 근거: 02번 문서 8번 탭 카드 7·13, 10번 탭 카드 5(v1.60 보강 — 원래 각자 인라인 튜플/미정의
/// NodeRef를 쓰던 것을 여기 하나로 통일).
/// </summary>
/// <param name="FlowId">이 노드가 속한 Flow 탭의 Id(<see cref="FlowDefinition.Id"/>).</param>
/// <param name="NodeId">노드의 고유 식별자(<see cref="NodeConfig.Id"/>).</param>
/// <param name="NodeName">캔버스에 표시되는 노드 이름 — 결과 목록을 사람이 읽을 때 사용(예: "이 태그를 참조하는 노드: PlcTagReadNode1").</param>
/// <example>
/// <code>
/// // 1) IStructureService.FindNodesByTagRef 결과 — 태그를 삭제하려는데 참조 중인 노드가 있는지 확인
/// IReadOnlyList&lt;NodeRef&gt; blockers = structureService.FindNodesByTagRef("tag-1");
/// if (blockers.Count > 0)
/// {
///     var names = string.Join(", ", blockers.Select(b => b.NodeName));
///     // "이 태그는 {names}에서 사용 중이라 삭제할 수 없습니다" 안내
/// }
///
/// // 2) IFlowNodeIndex.FindNodesBySequenceId 결과 — 여러 곳에서 같은 시퀀스를 호출하면 선택 목록으로 표시
/// IReadOnlyList&lt;NodeRef&gt; callers = flowNodeIndex.FindNodesBySequenceId("seq-1");
/// // callers.Count == 1이면 바로 캔버스 이동, 여러 개면 드롭다운으로 선택
/// </code>
/// </example>
public sealed record NodeRef(string FlowId, string NodeId, string NodeName);
