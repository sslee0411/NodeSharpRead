using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="NodeConfig"/>(CT-02b, 02번 설계 문서 2번 탭 카드 10)에 대한 단위 테스트입니다.
/// 완료 기준: flows.json 전체 필드를 포함해 System.Text.Json으로 직렬화/역직렬화 왕복 시
/// 데이터 손실이 없는지 확인.
/// </summary>
public class NodeConfigTests
{
    [Fact]
    public void 필수_필드와_기본값이_생성자에서_올바르게_설정된다()
    {
        var config = new NodeConfig(
            Id: "n1", Type: "function", Name: "온도 변환", FlowId: "f1",
            Properties: new Dictionary<string, object?>());

        Assert.Equal("n1", config.Id);
        Assert.Equal("function", config.Type);
        Assert.Equal("온도 변환", config.Name);
        Assert.Equal("f1", config.FlowId);

        // 생략된 매개변수는 02번 문서 정식 선언의 기본값을 그대로 따라야 한다
        Assert.Equal(DispatchMode.Sequential, config.OutputDispatch);
        Assert.Equal(1, config.MaxConcurrency);
        Assert.Null(config.CredentialRefId);
        Assert.False(config.Disabled);
    }

    [Fact]
    public void Properties_딕셔너리가_내용은_같아도_참조가_다르면_record_동등성은_다르다고_판단한다()
    {
        // record의 자동 생성 Equals는 컬렉션 타입 필드를 EqualityComparer<T>.Default로 비교하는데,
        // Dictionary는 값 동등성을 오버라이드하지 않으므로 내용이 같아도 참조가 다르면 다른 값으로
        // 취급된다 — NodeConfig를 record로 선언한 02번 문서 설계의 알려진 특성(버그 아님, 캐노니컬
        // 동등성 비교가 필요하면 별도 헬퍼가 있어야 함을 문서화하기 위한 테스트).
        var propsA = new Dictionary<string, object?> { ["x"] = 1 };
        var propsB = new Dictionary<string, object?> { ["x"] = 1 };

        var a = new NodeConfig("n1", "function", "이름", "f1", propsA);
        var b = new NodeConfig("n1", "function", "이름", "f1", propsB);

        Assert.NotEqual(a, b); // Properties 참조가 다르므로 record 전체 동등성은 성립하지 않음
        Assert.Equal(a.Id, b.Id); // 개별 필드는 당연히 같음
    }

    [Fact]
    public void SystemTextJson_왕복_시_스칼라_필드가_모두_보존된다()
    {
        var original = new NodeConfig(
            Id: "n1", Type: "function", Name: "온도 변환", FlowId: "f1",
            Properties: new Dictionary<string, object?>(),
            OutputDispatch: DispatchMode.Parallel,
            MaxConcurrency: 4,
            CredentialRefId: "cred-1",
            Disabled: true);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodeConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Type, restored.Type);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.FlowId, restored.FlowId);
        Assert.Equal(original.OutputDispatch, restored.OutputDispatch);
        Assert.Equal(original.MaxConcurrency, restored.MaxConcurrency);
        Assert.Equal(original.CredentialRefId, restored.CredentialRefId);
        Assert.Equal(original.Disabled, restored.Disabled);
    }

    [Fact]
    public void SystemTextJson_왕복_시_Properties_값이_JsonElement로_보존된다()
    {
        // 클래스 XML 주석에 명시한 대로, System.Text.Json은 object? 타입 값을 JsonElement로
        // 역직렬화한다(Newtonsoft.Json이 Msg에서 원래 CLR 타입으로 복원해주는 것과 다름).
        // 이 테스트는 그 실제 동작을 증명하고, 값 자체는 손실 없이 남아있는지 확인한다.
        var original = new NodeConfig(
            Id: "n1", Type: "function", Name: "이름", FlowId: "f1",
            Properties: new Dictionary<string, object?>
            {
                ["code"] = "return msg.payload * 1.8 + 32;",
                ["timeoutMs"] = 5000,
                ["enabled"] = true,
            });

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodeConfig>(json);

        Assert.NotNull(restored);

        var codeElement = Assert.IsType<JsonElement>(restored!.Properties["code"]);
        Assert.Equal("return msg.payload * 1.8 + 32;", codeElement.GetString());

        var timeoutElement = Assert.IsType<JsonElement>(restored.Properties["timeoutMs"]);
        Assert.Equal(5000, timeoutElement.GetInt32());

        var enabledElement = Assert.IsType<JsonElement>(restored.Properties["enabled"]);
        Assert.True(enabledElement.GetBoolean());
    }
}
