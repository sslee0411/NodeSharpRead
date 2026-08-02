using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

// 한글명: 속성 필드
/// <summary>
/// 노드 속성 편집 다이얼로그에 표시할 입력 필드 1개를 정의합니다. "PropertySchema"는 별도 타입이
/// 아니라 이 레코드의 목록(<c>IReadOnlyList&lt;PropertyField&gt;</c>)을 가리키는 관례적 이름입니다
/// — <c>INodeTypeDescriptor.PropertySchema</c>·각 노드의 <c>BuildPropertySchema()</c>가 모두 이 형태를
/// 반환합니다.
/// 설계 근거: 02번 문서 9번 탭 카드 3.
/// </summary>
/// <remarks>
/// <b>왜 HelpText/Example이 필수급인가(개발 지침 4번)</b>: 초보 사용자는 "Timeout"이라는 라벨만
/// 보고는 단위가 ms인지 sec인지, 0을 넣으면 무한대기인지 알 수 없습니다. 그러나 C# 언어 자체는
/// 빈 문자열 기본값을 컴파일 오류로 막지 못하므로, <see cref="PropertySchemaValidator"/>가 런타임
/// 검증으로 이를 표면화합니다(노드 타입 등록 시점에 호출하는 것을 권장).
/// </remarks>
/// <example>
/// <code>
/// // Timeout 필드를 "설명 없이" 만들면 사용자가 단위를 몰라 잘못된 값을 입력하기 쉽다 — 나쁜 예:
/// //   new PropertyField("timeout", "Timeout", PropertyFieldType.Number)
/// //
/// // HelpText/Example을 채우면 캔버스에서 바로 이해 가능:
/// var timeoutField = new PropertyField(
///     Key: "timeout", Label: "타임아웃", Type: PropertyFieldType.Number,
///     Required: true, DefaultValue: "5000",
///     HelpText: "서버 응답을 기다리는 최대 시간(밀리초, ms)입니다. 이 시간이 지나면 자동으로 " +
///               "실패 처리되고 msg가 2번째(에러) 출력 포트로 나갑니다. 0을 입력하면 무제한 대기입니다.",
///     Example: "예: 5000 (5초), 30000 (30초, 느린 서버용)");
/// </code>
/// </example>
public sealed record PropertyField(
    string Key,
    string Label,
    PropertyFieldType Type,
    bool Required = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    string HelpText = "",
    string Example = "");

// 한글명: 속성 스키마 검증기
/// <summary>
/// <see cref="PropertyField"/> 목록에서 <see cref="PropertyField.HelpText"/>/<see cref="PropertyField.Example"/>이
/// 비어있는 필드를 찾아 "문서화 누락"을 표면화하는 헬퍼입니다. 노드 타입 등록(<c>NodeTypeRegistry</c>,
/// <c>CT-06b</c>) 또는 배포 전 검사(<c>OP-04</c> FlowLinter, Phase 10)에서 호출하는 것을 권장합니다.
/// </summary>
/// <example>
/// <code>
/// var fields = new[] { new PropertyField("timeout", "타임아웃", PropertyFieldType.Number) };   // HelpText/Example 없음
/// IReadOnlyList&lt;string&gt; undocumented = PropertySchemaValidator.GetUndocumentedFieldKeys(fields);
/// // undocumented == ["timeout"] — 개발 지침 4번 위반을 조기에 발견
/// </code>
/// </example>
public static class PropertySchemaValidator
{
    /// <summary><see cref="PropertyField.HelpText"/> 또는 <see cref="PropertyField.Example"/>이 비어있거나 공백뿐인 필드의 Key 목록을 반환합니다. 모두 문서화돼 있으면 빈 목록을 반환합니다.</summary>
    public static IReadOnlyList<string> GetUndocumentedFieldKeys(IReadOnlyList<PropertyField> fields) =>
        fields.Where(f => string.IsNullOrWhiteSpace(f.HelpText) || string.IsNullOrWhiteSpace(f.Example))
              .Select(f => f.Key)
              .ToList();
}
