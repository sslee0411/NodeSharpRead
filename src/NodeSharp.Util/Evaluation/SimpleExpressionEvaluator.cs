using System.Globalization;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Util.Evaluation;

/// <summary>
/// Class명 : 간이 수식 평가기
/// 역활 및 기능 : 사칙연산·괄호·msg 필드 참조로 이루어진 간단한 수식 문자열을 계산하는 임시 평가기
///
/// <c>NodeSharp.Contracts.Enums.TypedValueSource.Expression</c>을 위한 임시 구현입니다. 정식 수식 실행기는
/// <c>FN-01</c>(Function 노드의 NCalc 실행기)이 맡을 예정이지만(<c>TypedValue.cs</c> XML 문서가 이미
/// "Expression은 NCalc 실행기 재사용"이라고 명시), FN-01이 아직 <c>⏳ 대기</c>라 NR-04(Switch 노드)가
/// 그것을 기다리지 않고 자체적으로 아주 단순한 사칙연산 평가기를 임시로 만들었습니다(사용자 확인,
/// 2026-08 세션 — "간단한 산술식 평가기를 임시로 만듦"). FN-01이 완료되면 이 클래스는 NCalc 기반
/// 구현으로 교체될 예정입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>지원 문법: 숫자 리터럴, <c>+</c>/<c>-</c>/<c>*</c>/<c>/</c>/<c>%</c> 사칙연산, 괄호, 단항
/// <c>+</c>/<c>-</c>, 작은따옴표/큰따옴표 문자열 리터럴, <c>msg</c> 필드 참조(점 표기, 예:
/// <c>payload.temp</c> — <see cref="TypedValueEvaluator"/>의 msg 필드 경로 해석과 동일한 규칙).</item>
/// <item>지원하지 않는 문법(비교 연산자, 함수 호출, 삼항 연산자 등)은 <see cref="FormatException"/>을
/// 던집니다 — 조용히 잘못된 값을 반환하지 않습니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var msg = new Msg { Payload = 20.0 };
///
/// // 1) TypedValueSource.cs 예제와 동일한 수식 — msg 필드 참조 + 사칙연산 혼합
/// object? f = SimpleExpressionEvaluator.Evaluate("payload * 1.8 + 32", msg);   // 68.0(섭씨 20도 → 화씨)
///
/// // 2) 괄호와 문자열 리터럴
/// object? s = SimpleExpressionEvaluator.Evaluate("'threshold-' + payload", msg);   // "threshold-20"(문자열 연결)
///
/// // 3) 지원하지 않는 문법은 예외
/// // SimpleExpressionEvaluator.Evaluate("payload > 10", msg);   // FormatException
/// </code>
/// </example>
public static class SimpleExpressionEvaluator
{
    /// <summary><paramref name="expression"/>을 <paramref name="msg"/> 기준으로 계산해 결과 값(<see cref="double"/> 또는 <see cref="string"/>)을 반환합니다.</summary>
    public static object? Evaluate(string expression, Msg msg)
    {
        var parser = new Parser(expression ?? string.Empty, msg);
        var result = parser.ParseExpression();
        parser.SkipWhiteSpace();
        if (!parser.AtEnd)
        {
            throw new FormatException($"수식을 끝까지 해석하지 못했습니다: '{expression}' (남은 부분: '{parser.Remainder}')");
        }

        return result;
    }

    /// <summary>재귀 하강(recursive descent) 방식의 사설 파서 — 이 클래스 밖으로 노출하지 않습니다.</summary>
    private sealed class Parser
    {
        private readonly string _text;
        private readonly Msg _msg;
        private int _pos;

        public Parser(string text, Msg msg)
        {
            _text = text;
            _msg = msg;
        }

        public bool AtEnd => _pos >= _text.Length;

        public string Remainder => _text[_pos..];

        public void SkipWhiteSpace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        // expr := term (('+' | '-') term)*
        public object? ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhiteSpace();
                if (Peek() == '+')
                {
                    _pos++;
                    left = Add(left, ParseTerm());
                }
                else if (Peek() == '-')
                {
                    _pos++;
                    left = ToDouble(left) - ToDouble(ParseTerm());
                }
                else
                {
                    return left;
                }
            }
        }

        // term := factor (('*' | '/' | '%') factor)*
        private object? ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                SkipWhiteSpace();
                var c = Peek();
                if (c == '*')
                {
                    _pos++;
                    left = ToDouble(left) * ToDouble(ParseFactor());
                }
                else if (c == '/')
                {
                    _pos++;
                    left = ToDouble(left) / ToDouble(ParseFactor());
                }
                else if (c == '%')
                {
                    _pos++;
                    left = ToDouble(left) % ToDouble(ParseFactor());
                }
                else
                {
                    return left;
                }
            }
        }

        // factor := ('+' | '-') factor | primary
        private object? ParseFactor()
        {
            SkipWhiteSpace();
            if (Peek() == '-')
            {
                _pos++;
                return -ToDouble(ParseFactor());
            }

            if (Peek() == '+')
            {
                _pos++;
                return ParseFactor();
            }

            return ParsePrimary();
        }

        // primary := NUMBER | STRING | IDENTIFIER('.' IDENTIFIER)* | '(' expr ')'
        private object? ParsePrimary()
        {
            SkipWhiteSpace();
            if (AtEnd)
            {
                throw new FormatException("수식이 예상보다 일찍 끝났습니다.");
            }

            var c = Peek();
            if (c == '(')
            {
                _pos++;
                var value = ParseExpression();
                SkipWhiteSpace();
                Expect(')');
                return value;
            }

            if (c is '\'' or '"')
            {
                return ParseStringLiteral(c);
            }

            if (char.IsDigit(c))
            {
                return ParseNumber();
            }

            if (char.IsLetter(c) || c == '_')
            {
                return ParseIdentifierPath();
            }

            throw new FormatException($"수식에서 인식할 수 없는 문자 '{c}'를 만났습니다(위치 {_pos}).");
        }

        private object? ParseStringLiteral(char quote)
        {
            _pos++; // 여는 따옴표
            var start = _pos;
            while (_pos < _text.Length && _text[_pos] != quote)
            {
                _pos++;
            }

            if (_pos >= _text.Length)
            {
                throw new FormatException("문자열 리터럴의 닫는 따옴표를 찾지 못했습니다.");
            }

            var value = _text[start.._pos];
            _pos++; // 닫는 따옴표
            return value;
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                _pos++;
            }

            return double.Parse(_text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private object? ParseIdentifierPath()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] is '_' or '.'))
            {
                _pos++;
            }

            var path = _text[start.._pos];
            return TypedValueEvaluator.ResolveMsgFieldPath(path, _msg);
        }

        private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw new FormatException($"'{c}'가 필요한 위치(수식 위치 {_pos})에서 다른 문자를 만났습니다.");
            }

            _pos++;
        }

        private static double ToDouble(object? value) =>
            ValueComparer.TryToDouble(value, out var d) ? d : throw new FormatException($"'{value}' 값은 숫자로 변환할 수 없어 사칙연산에 쓸 수 없습니다.");

        private static object? Add(object? left, object? right)
        {
            // 두 값이 모두 숫자로 변환되면 숫자 덧셈, 하나라도 안 되면(문자열 리터럴 포함) 문자열 연결(JS의 '+' 관용과 동일).
            if (ValueComparer.TryToDouble(left, out var dl) && ValueComparer.TryToDouble(right, out var dr))
            {
                return dl + dr;
            }

            return Convert.ToString(left, CultureInfo.InvariantCulture) + Convert.ToString(right, CultureInfo.InvariantCulture);
        }
    }
}
