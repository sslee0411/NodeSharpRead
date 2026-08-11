using System.Globalization;

namespace NodeSharp.Util.Evaluation;

/// <summary>
/// Class명 : 값 비교기
/// 역활 및 기능 : 타입이 다른 두 런타임 값을 숫자 우선으로 비교·동등 판정하는 공용 헬퍼
///
/// Switch(NR-04)의 lt/lte/gt/gte/btwn/eq/neq 연산자, 그리고 앞으로 같은 비교가 필요한 Change/Range
/// (NR-12a/NR-12b, <c>NodeSharp.Contracts.Models.TypedValue</c>의 XML 문서가 이미 이 세 노드를 나란히
/// 언급)에서 공유하는 값 비교 로직입니다. Node-RED의 JS <c>&lt;</c>/<c>&gt;</c>가 양쪽을 숫자로
/// 변환할 수 있으면 숫자로, 아니면 문자열로 비교하는 동작을 C#으로 재현합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Compare"/>는 양쪽 모두 숫자로 변환 가능하면 숫자 비교, 아니면 <c>ToString()</c>
/// 결과를 서수(ordinal) 문자열 비교합니다.</item>
/// <item><see cref="LooseEquals"/>는 둘 다 <c>null</c>이면 같음, 하나만 <c>null</c>이면 다름으로
/// 처리한 뒤 나머지는 <see cref="Compare"/>와 같은 규칙(숫자 우선)을 따릅니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 숫자로 변환되는 문자열끼리는 숫자 비교(문자열 "9"가 "10"보다 작다고 나옴 — 사전순이 아님)
/// int cmp = ValueComparer.Compare("9", "10");   // -1 (9 &lt; 10)
///
/// // 2) 숫자로 변환 안 되는 값은 문자열 비교로 대체
/// int cmp2 = ValueComparer.Compare("apple", "banana");   // -1 (사전순 "apple" &lt; "banana")
///
/// // 3) 느슨한 동등 비교 — 42(int)와 "42"(string)는 숫자로 변환해 같다고 판정
/// bool eq = ValueComparer.LooseEquals(42, "42");   // true
/// </code>
/// </example>
public static class ValueComparer
{
    /// <summary><paramref name="value"/>를 <see cref="double"/>로 변환할 수 있으면 <paramref name="result"/>에 담아 <c>true</c>를, 아니면(문자열이 아닌 숫자 형태가 아니거나 <c>null</c>/<c>bool</c>이면) <c>false</c>를 반환합니다.</summary>
    public static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary><paramref name="a"/>가 <paramref name="b"/>보다 작으면 음수, 같으면 0, 크면 양수를 반환합니다(양쪽 모두 숫자로 변환되면 숫자 비교, 아니면 문자열 비교).</summary>
    public static int Compare(object? a, object? b)
    {
        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
        {
            return da.CompareTo(db);
        }

        var sa = Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty;
        var sb = Convert.ToString(b, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.CompareOrdinal(sa, sb);
    }

    /// <summary><paramref name="a"/>와 <paramref name="b"/>가 같은 값으로 볼 수 있는지 판정합니다(숫자로 변환되면 숫자 동등, 아니면 문자열 동등, 둘 다 <c>null</c>이면 같음).</summary>
    public static bool LooseEquals(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
        {
            return da.Equals(db);
        }

        return string.Equals(
            Convert.ToString(a, CultureInfo.InvariantCulture),
            Convert.ToString(b, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}
