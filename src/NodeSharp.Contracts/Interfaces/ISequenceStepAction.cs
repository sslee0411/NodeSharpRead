using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 시퀀스 단계 동작 계약
/// 역활 및 기능 : SequenceExecutor가 SequenceStepDto.ActionType 문자열로부터 실제로 실행할 동작을 감싸는 전략 패턴 인터페이스
///
/// <c>SequenceExecutor</c>(<c>SQ-01</c>)가 <see cref="SequenceStepDto.ActionType"/> 문자열로 찾아 실행하는
/// 동작 1개를 감싸는 전략 패턴 인터페이스입니다 — <see cref="IFunctionExecutor"/>가 Function 노드의
/// "표현식이냐 코드냐" 분기를 인터페이스로 뽑아낸 것과 동일한 이유로, "이 단계가 실제로 무엇을 하는가"
/// (PLC에 값 쓰기, 다른 시퀀스 호출, 알림 발송 등)를 <c>SequenceExecutor</c> 안에서 if/else로 직접
/// 분기하지 않고 이 인터페이스로 뽑아냈습니다 — 새 동작 종류가 추가돼도 <c>SequenceExecutor</c> 코드는
/// 건드릴 필요 없이 구현체 하나(예: <c>PlcWriteStepAction</c>)만 더 만들어 호출자(등록 딕셔너리)에
/// 키만 추가하면 됩니다.
/// 설계 근거: 02번 문서 11번 탭 카드 5(Sequence 설계), <see cref="SequenceStepDto.ActionType"/> XML 문서.
/// </summary>
/// <remarks>
/// <b>(SQ-01) 범위 축소</b>: <see cref="SequenceStepDto.ActionType"/> XML 문서 원문은 "lssLib.Sequence.SequenceStepBase
/// 구현체의 타입명"이라고 설명하지만, 이 저장소에는 lssLib.Sequence 소스 자체가 없고(grep 확인, 2026-09
/// 세션) NodeSharpRead는 이미 자체 <see cref="SequenceDefinition"/>/<see cref="SequenceStepDto"/> 모델을
/// Contracts에 갖고 있어(평면 단계 목록·NCalc 진입조건·OnFailStepId/OnTimeoutStepId 분기 — lssLib.Sequence의
/// SequenceGroup/StepExecutionMode 그룹·병렬 개념과 1:1로 대응하지 않음) 사용자 확인(2026-09 세션,
/// "자체 모델 기반 구현")에 따라 lssLib.Sequence 그 자체를 이식하는 대신, 이 자체 모델을 그대로 실행하는
/// 네이티브 <c>SequenceExecutor</c>를 구현합니다. <see cref="ExecuteAsync"/>가 문자열 키(ActionType) →
/// 구현체 조회 방식을 쓰는 것은 lssLib.Sequence의 "타입명으로 스텝을 식별한다"는 원래 의도를 최대한
/// 보존하기 위함입니다. 실제 PLC 쓰기 등 구체 동작 구현체(예: <c>PlcWriteStepAction</c>)는 아직 없습니다
/// — <c>SequenceExecutor</c>의 완료 기준("3단계 이상을 순서대로 실행했을 때 순서·타임아웃·실패시이동
/// 규칙대로 진행되는지")은 순수 엔진 동작만 요구하고, 구체 동작은 그 엔진을 실제로 쓰는 후속 Step(예:
/// PLC 쓰기 노드/시퀀스 연동)에서 필요해지는 대로 추가될 예정입니다(RT-09a→RT-09b가 "재료 클래스부터
/// 독립 완성, 실제 연동은 나중"으로 쌓아온 것과 동일한 원칙).
/// </remarks>
/// <example>
/// <code>
/// public sealed class PlcWriteStepAction : ISequenceStepAction
/// {
///     public Task&lt;bool&gt; ExecuteAsync(SequenceStepDto step, CancellationToken ct)
///     {
///         var tagId = (string)step.ActionParams["tagId"]!;
///         var value = step.ActionParams["value"];
///         // ... 실제 PLC 쓰기 ...
///         return Task.FromResult(true);   // false를 반환하면 SequenceExecutor가 OnFailStepId로 분기
///     }
/// }
///
/// var actions = new Dictionary&lt;string, ISequenceStepAction&gt; { ["PlcWriteStep"] = new PlcWriteStepAction() };
/// </code>
/// </example>
public interface ISequenceStepAction
{
    /// <summary>
    /// <paramref name="step"/>(주로 <see cref="SequenceStepDto.ActionParams"/>)를 바탕으로 이 단계의 실제
    /// 동작을 실행하고 성공 여부를 반환합니다. <c>false</c>를 반환하거나 예외를 던지면 <c>SequenceExecutor</c>가
    /// 실패로 간주해 <see cref="SequenceStepDto.OnFailStepId"/>로 분기합니다(없으면 시퀀스 전체가
    /// <c>SequenceState.Faulted</c>로 종료).
    /// </summary>
    Task<bool> ExecuteAsync(SequenceStepDto step, CancellationToken ct);
}
