using System.Globalization;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Util.Evaluation;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 간이 시퀀스 진입조건 평가기
/// 역활 및 기능 : 리터럴 true/false와 "식별자 비교연산자 값" 형태의 단일 비교식만 계산하는 임시 ISequenceTriggerEvaluator 구현체
///
/// <see cref="ISequenceTriggerEvaluator"/>의 임시 구현체입니다. 정식 구현은 NCalc 기반이어야 하지만
/// (<see cref="ISequenceTriggerEvaluator"/> XML 문서의 "(SQ-01) 범위 축소" 참고 — NCalc 패키지 의존성을
/// 노드 플러그인 프로젝트 밖(Runtime 등 공유 코어)에 두지 않는 <c>NCalcFunctionExecutor</c>(FN-01) 선례),
/// <c>SimpleExpressionEvaluator</c>(NR-04, Switch 노드가 FN-01을 기다리지 않고 만든 임시 사칙연산
/// 평가기)와 같은 전례로 이 클래스도 <c>SequenceExecutor</c>(SQ-01)의 완료 기준(순서·타임아웃·
/// 실패시이동 규칙 확인)에 필요한 최소한의 비교식만 지원하는 임시 구현입니다. 실제 sequences.json을
/// 로드·실행하는 후속 Step에서 NCalc 기반 구현체로 교체될 예정입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>지원 문법: 공백 제거 후 대소문자 무시 <c>true</c>/<c>false</c> 리터럴(항상 참/거짓), 또는
/// <c>식별자 (==|!=|&lt;=|&gt;=|&lt;|&gt;) 값</c> 단일 비교식 1개(불리언 결합자 <c>&amp;&amp;</c>/<c>||</c>는
/// 지원하지 않음 — <see cref="Models.SequenceStepDto"/> 예제 코드도 <c>"tag-valve1 == true"</c>처럼 항상
/// 단일 비교식만 씀). 식별자는 <paramref name="resolveVariable"/>(주로 태그 Id)로 조회하고, 값은
/// <c>true</c>/<c>false</c>/숫자/작은따옴표 또는 큰따옴표 문자열 리터럴을 지원합니다.</item>
/// <item>비교는 <see cref="ValueComparer"/>(NR-04, Switch 노드와 동일한 공용 비교 로직 — 양쪽 모두 숫자로
/// 변환되면 숫자 비교, 아니면 문자열 비교)를 그대로 재사용합니다.</item>
/// <item>지원하지 않는 문법은 <see cref="FormatException"/>을 던집니다 — <c>SimpleExpressionEvaluator</c>와
/// 동일하게 조용히 잘못된 값을 반환하지 않습니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var evaluator = new SimpleSequenceTriggerEvaluator();
///
/// evaluator.Evaluate("true", _ =&gt; null);                              // true — 시퀀스 시작 즉시 진입
/// evaluator.Evaluate("tag-valve1 == true", id =&gt; id == "tag-valve1" ? (object)true : null);   // true
/// evaluator.Evaluate("pressure &lt; 100", id =&gt; id == "pressure" ? (object)42.0 : null);        // true
/// </code>
/// </example>
public sealed class SimpleSequenceTriggerEvaluator : ISequenceTriggerEvaluator
{
    private static readonly string[] Operators = { "==", "!=", "<=", ">=", "<", ">" };

    /// <inheritdoc/>
    public bool Evaluate(string expression, Func<string, object?> resolveVariable)
    {
        var text = (expression ?? string.Empty).Trim();

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var op in Operators)
        {
            var idx = text.IndexOf(op, StringComparison.Ordinal);
            if (idx <= 0)
            {
                continue;
            }

            var left = text[..idx].Trim();
            var right = text[(idx + op.Length)..].Trim();
            var leftValue = resolveVariable(left);
            var rightValue = ParseLiteral(right);

            return op switch
            {
                "==" => ValueComparer.LooseEquals(leftValue, rightValue),
                "!=" => !ValueComparer.LooseEquals(leftValue, rightValue),
                "<=" => ValueComparer.Compare(leftValue, rightValue) <= 0,
                ">=" => ValueComparer.Compare(leftValue, rightValue) >= 0,
                "<" => ValueComparer.Compare(leftValue, rightValue) < 0,
                ">" => ValueComparer.Compare(leftValue, rightValue) > 0,
                _ => throw new FormatException($"지원하지 않는 비교 연산자입니다: '{op}'"),
            };
        }

        throw new FormatException($"진입조건 식을 해석하지 못했습니다: '{expression}' (지원 문법: true/false 리터럴, 또는 '식별자 비교연산자 값' 단일 비교식)");
    }

    private static object? ParseLiteral(string text)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length >= 2 && ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"')))
        {
            return text[1..^1];
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return text;
    }
}
