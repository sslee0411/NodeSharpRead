using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

// 한글명: 플로우 노드 역색인 계약
/// <summary>
/// 특정 시퀀스를 호출하는 Flow 노드(<c>SequenceTriggerNode</c>)를 역방향으로 찾는 계약입니다.
/// <see cref="IStructureService.FindNodesByTagRef"/>와 대칭 구조로, Sequence Editor 창에서
/// "이 시퀀스를 호출하는 Flow 노드 보기"를 누르면 이 인터페이스로 캔버스를 역추적합니다.
/// 설계 근거: 02번 문서 10번 탭 카드 5(v1.60에서 반환 타입을 <see cref="NodeRef"/>로 확정 — 원래
/// 정의 없이 쓰이던 NodeRef를 8번 탭 카드 7과 공유하는 공통 타입으로 정리).
/// </summary>
/// <example>
/// <code>
/// // Sequence Editor 창 툴바 → "이 시퀀스를 호출하는 Flow 노드 보기"
/// IReadOnlyList&lt;NodeRef&gt; callers = flowNodeIndex.FindNodesBySequenceId("seq-1");
///
/// if (callers.Count == 1)
/// {
///     // 결과가 1개면 바로 MainWindow로 전환 + 해당 노드로 캔버스 스크롤 + 하이라이트
/// }
/// else if (callers.Count > 1)
/// {
///     // 여러 곳에서 같은 시퀀스를 호출하면(예: 여러 라인이 공통 절차 재사용) 목록에서 선택
/// }
/// </code>
/// </example>
public interface IFlowNodeIndex
{
    /// <summary>지정한 시퀀스를 호출하는 <c>SequenceTriggerNode</c>를 모든 Flow에서 찾아 반환합니다.</summary>
    IReadOnlyList<NodeRef> FindNodesBySequenceId(string sequenceId);
}
