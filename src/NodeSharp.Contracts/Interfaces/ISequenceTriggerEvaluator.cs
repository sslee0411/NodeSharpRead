namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 시퀀스 진입조건 평가기 계약
/// 역활 및 기능 : SequenceExecutor가 SequenceStepDto.TriggerExpression 문자열을 계산하는 로직을 감싸는 전략 패턴 인터페이스
///
/// <c>SequenceExecutor</c>(<c>SQ-01</c>)가 <c>SequenceStepDto.TriggerExpression</c>(진입 조건)을 계산할 때
/// 쓰는 전략 패턴 인터페이스입니다. <see cref="IFunctionExecutor"/>와 동일한 이유(표현식 평가 로직을
/// 엔진 코드에서 분리)로 뽑아냈습니다.
/// </summary>
/// <remarks>
/// <b>(SQ-01) 범위 축소</b>: <c>SequenceStepDto.TriggerExpression</c> XML 문서는 "NCalc 표현식(6번 탭
/// NCalc 재사용)"이라고 명시하지만, <c>NCalcFunctionExecutor</c>(FN-01)의 XML 문서(<see cref="IFunctionExecutor"/>
/// 참고)가 이미 "NCalc 패키지 의존성은 실제로 쓰는 노드 플러그인 프로젝트 안에만 두고 Contracts/Util에
/// 퍼뜨리지 않는다"는 선례를 남겼습니다 — <c>NodeSharp.Runtime</c>(Editor·Runner 공용 코어 레이어)에
/// <c>SequenceExecutor</c>가 있다 보니 같은 이유로 NCalc를 직접 참조하지 않는 편이 선례와 일관됩니다.
/// 이 인터페이스를 둔 것도 그 때문 — 기본 구현체(<c>SimpleSequenceTriggerEvaluator</c>, Runtime)는
/// <c>NR-04</c>(Switch 노드)가 FN-01을 기다리지 않고 만들었던 <c>SimpleExpressionEvaluator</c>와 동일한
/// 전례로 임시 비교 평가기이며, 실제 NCalc 기반 구현체는 sequences.json을 실제로 로드·실행하는 후속
/// Step(SQ-02 이후, Sequence Editor/저장 연동)에서 노드 플러그인과 같은 위치 원칙에 따라 별도로 추가될
/// 예정입니다.
/// </remarks>
/// <example>
/// <code>
/// public sealed class NCalcSequenceTriggerEvaluator : ISequenceTriggerEvaluator
/// {
///     public bool Evaluate(string expression, Func&lt;string, object?&gt; resolveVariable)
///     {
///         var expr = new NCalc.Expression(expression);
///         expr.EvaluateParameter += (name, args) =&gt; args.Result = resolveVariable(name);
///         return Convert.ToBoolean(expr.Evaluate());
///     }
/// }
/// </code>
/// </example>
public interface ISequenceTriggerEvaluator
{
    /// <summary>
    /// <paramref name="expression"/>을 계산해 진입 조건 충족 여부를 반환합니다. 식 안의 식별자(태그 Id 등)는
    /// <paramref name="resolveVariable"/>로 조회합니다(찾지 못하면 <c>null</c> 반환 — 평가기 구현체가 처리).
    /// 문법 오류 등은 예외로 던지며, 잡는 책임은 호출자(<c>SequenceExecutor</c>)에게 있습니다.
    /// </summary>
    bool Evaluate(string expression, Func<string, object?> resolveVariable);
}
