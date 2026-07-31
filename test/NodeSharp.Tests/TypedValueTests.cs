using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TypedValueSource"/>/<see cref="TypedValue"/>(CT-08, 02번 설계 문서 9번 탭 카드 3)에
/// 대한 단위 테스트입니다. 완료 기준 중 "Editor가 Source별로 다른 입력 컨트롤로 전환"은 WPF UI
/// 구현(Editor 단계) 몫이라 이 Step에서는 검증 대상이 아니며, "NodeConfig 재로드 후에도 Source·
/// Value가 복원되는지"를 System.Text.Json 왕복으로 검증합니다.
/// </summary>
public class TypedValueTests
{
    [Theory]
    [InlineData(TypedValueSource.Fixed, "85.0")]
    [InlineData(TypedValueSource.MsgField, "payload.temp")]
    [InlineData(TypedValueSource.FlowContext, "lastAlarmLevel")]
    [InlineData(TypedValueSource.GlobalContext, "lineRunning")]
    [InlineData(TypedValueSource.EnvVar, "MAX_TEMP")]
    [InlineData(TypedValueSource.Expression, "payload * 1.8 + 32")]
    public void TypedValue_6가지_Source_모두_SystemTextJson_왕복_후_보존된다(TypedValueSource source, string value)
    {
        var original = new TypedValue(source, value);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<TypedValue>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void TypedValueSource_6가지_값이_모두_존재한다()
    {
        var values = Enum.GetValues<TypedValueSource>();

        Assert.Equal(6, values.Length);
        Assert.Contains(TypedValueSource.Fixed, values);
        Assert.Contains(TypedValueSource.MsgField, values);
        Assert.Contains(TypedValueSource.FlowContext, values);
        Assert.Contains(TypedValueSource.GlobalContext, values);
        Assert.Contains(TypedValueSource.EnvVar, values);
        Assert.Contains(TypedValueSource.Expression, values);
    }

    [Fact]
    public void PropertyFieldType_TypedValue는_이미_정의돼_있다()
    {
        // CT-01b에서 이미 추가된 값 — CT-08은 TypedValueSource/TypedValue만 신규 정의
        Assert.Contains(PropertyFieldType.TypedValue, Enum.GetValues<PropertyFieldType>());
    }
}
