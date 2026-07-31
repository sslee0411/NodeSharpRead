namespace NodeSharp.Util;

/// <summary>
/// <c>major.minor.patch</c> 형식의 시맨틱 버전 문자열을 비교하는 최소 유틸리티입니다.
/// <c>NodeTypeRegistry</c>(Registry, <c>CT-06b</c>)가 플러그인이 요구하는 Contracts 버전과 실제 로드된
/// Contracts 버전을 비교해 로드 가능 여부를 판단할 때 사용합니다.
/// 설계 근거: 02번 문서 10번 탭 카드 8(플러그인 버전 호환성 검사). lssLib에는 대응 항목이 없는
/// 신규 도입 유틸리티입니다(<c>INodeContext</c>·<c>CT-04a</c>와 동일한 유형).
/// </summary>
/// <remarks>
/// 호환성 판단 규칙은 SemVer의 "주 버전(Major)이 같으면 API 호환"이라는 통상적 관례를 따릅니다 —
/// Breaking Change는 Major 버전을 올릴 때만 발생한다고 간주합니다. Minor/Patch 차이는 항상 호환으로
/// 취급합니다(요구 버전이 실제보다 최신 Minor/Patch를 가리켜도 경고 없이 통과 — 엄격한 상한 검사는
/// 하지 않습니다).
/// </remarks>
/// <example>
/// <code>
/// SemVer.IsCompatible("1.2.0", "1.0.0");   // true  — 주 버전(1) 동일
/// SemVer.IsCompatible("2.0.0", "1.5.0");   // false — 주 버전 불일치(2 vs 1), 로드 거부 대상
/// SemVer.IsCompatible("1.0", "1.0.0");     // true  — 생략된 자리는 0으로 간주
/// </code>
/// </example>
public static class SemVer
{
    /// <summary>두 버전 문자열의 주 버전(Major)이 같으면 호환으로 판단합니다. 파싱할 수 없는 형식이면 <c>false</c>를 반환합니다.</summary>
    public static bool IsCompatible(string required, string actual)
    {
        if (!TryParseMajor(required, out var requiredMajor) || !TryParseMajor(actual, out var actualMajor))
            return false;

        return requiredMajor == actualMajor;
    }

    private static bool TryParseMajor(string version, out int major)
    {
        var firstSegment = version.Split('.')[0];
        return int.TryParse(firstSegment, out major);
    }
}
