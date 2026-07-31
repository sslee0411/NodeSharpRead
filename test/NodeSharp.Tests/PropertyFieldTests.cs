using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="PropertyField"/>/<see cref="PropertySchemaValidator"/>(CT-07, 02번 설계 문서 9번 탭
/// 카드 3)에 대한 단위 테스트입니다. 완료 기준이 요구하는 "HelpText/Example이 비어있으면 검증
/// 실패로 표면화되는지"를 <see cref="PropertySchemaValidator"/>로 직접 검증합니다.
/// </summary>
public class PropertyFieldTests
{
    [Fact]
    public void PropertyField_HelpText와_Example을_생략하면_빈_문자열이_기본값이다()
    {
        var field = new PropertyField(Key: "timeout", Label: "타임아웃", Type: PropertyFieldType.Number);

        Assert.Equal("", field.HelpText);
        Assert.Equal("", field.Example);
        Assert.False(field.Required);
        Assert.Null(field.DefaultValue);
        Assert.Null(field.Options);
    }

    [Fact]
    public void PropertyField_모든_필드를_채우면_그대로_보존된다()
    {
        var field = new PropertyField(
            Key: "method", Label: "Method", Type: PropertyFieldType.ComboBox,
            Required: true, DefaultValue: "GET", Options: new[] { "GET", "POST" },
            HelpText: "HTTP 메서드입니다.", Example: "예: GET");

        Assert.Equal("method", field.Key);
        Assert.True(field.Required);
        Assert.Equal("GET", field.DefaultValue);
        Assert.Equal(new[] { "GET", "POST" }, field.Options);
        Assert.Equal("HTTP 메서드입니다.", field.HelpText);
        Assert.Equal("예: GET", field.Example);
    }

    [Fact]
    public void PropertySchemaValidator_HelpText와_Example이_모두_채워진_필드는_문서화_누락이_없다()
    {
        var fields = new[]
        {
            new PropertyField("url", "URL", PropertyFieldType.Text, Required: true,
                HelpText: "요청 주소입니다.", Example: "https://api.example.com"),
        };

        var undocumented = PropertySchemaValidator.GetUndocumentedFieldKeys(fields);

        Assert.Empty(undocumented);
    }

    [Fact]
    public void PropertySchemaValidator_HelpText_또는_Example이_비면_해당_필드_Key가_누락_목록에_포함된다()
    {
        var fields = new[]
        {
            new PropertyField("timeout", "타임아웃", PropertyFieldType.Number),   // HelpText/Example 둘 다 없음 — 나쁜 예
            new PropertyField("url", "URL", PropertyFieldType.Text, HelpText: "요청 주소입니다.", Example: ""),   // Example만 없음
            new PropertyField("method", "Method", PropertyFieldType.ComboBox, HelpText: "메서드입니다.", Example: "예: GET"),   // 완전히 문서화됨
        };

        var undocumented = PropertySchemaValidator.GetUndocumentedFieldKeys(fields);

        Assert.Equal(new[] { "timeout", "url" }, undocumented);
    }
}
