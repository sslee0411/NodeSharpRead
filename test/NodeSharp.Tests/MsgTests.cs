using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="Msg"/>(CT-02a, 02번 설계 문서 2번 탭 카드 2)에 대한 단위 테스트입니다.
/// 이 Step의 완료 기준(03번 개발 Step맵.html)을 그대로 검증합니다:
/// (1) msg.payload/msg.topic 강타입 접근, (2) msg.Get&lt;T&gt;()/TryGet&lt;T&gt;() 동적 속성 접근,
/// (3) Clone() 후 원본 변경이 복제본에 영향을 주지 않음, (4) ToJson()/FromJson() 왕복 시 값 보존.
/// </summary>
public class MsgTests
{
    [Fact]
    public void Payload_And_Topic_강타입_접근이_동작한다()
    {
        var msg = new Msg { Payload = 42, Topic = "sensor/temp" };

        Assert.Equal(42, msg.Payload);
        Assert.Equal("sensor/temp", msg.Topic);
    }

    [Fact]
    public void Id는_생성_시_자동으로_부여되고_비어있지_않다()
    {
        var msg = new Msg();

        Assert.False(string.IsNullOrWhiteSpace(msg.Id));
    }

    [Fact]
    public void 서로_다른_Msg는_서로_다른_Id를_가진다()
    {
        var msg1 = new Msg();
        var msg2 = new Msg();

        Assert.NotEqual(msg1.Id, msg2.Id);
    }

    [Fact]
    public void 동적_필드_TrySetMember_TryGetMember가_dynamic으로_동작한다()
    {
        dynamic msg = new Msg();
        msg.customField = "hello";

        string result = msg.customField;

        Assert.Equal("hello", result);
    }

    [Fact]
    public void 존재하지_않는_동적_필드를_읽으면_null이고_예외가_없다()
    {
        dynamic msg = new Msg();

        object? result = msg.doesNotExist;

        Assert.Null(result);
    }

    [Fact]
    public void Get_T_는_타입이_일치하면_값을_반환하고_불일치하면_default를_반환한다()
    {
        var msg = new Msg();
        ((dynamic)msg).count = 5;

        var matched = msg.Get<int>("count");
        var mismatched = msg.Get<string>("count");     // 실제 타입은 int이므로 string 요청 시 default(null)
        var missing = msg.Get<int>("no_such_key");     // 필드 자체가 없음

        Assert.Equal(5, matched);
        Assert.Null(mismatched);
        Assert.Equal(0, missing);
    }

    [Fact]
    public void TryGet_T_는_존재하고_타입이_일치할_때만_true를_반환한다()
    {
        var msg = new Msg();
        ((dynamic)msg).count = 5;

        var found = msg.TryGet<int>("count", out var foundValue);
        var typeMismatch = msg.TryGet<string>("count", out var mismatchValue);
        var notFound = msg.TryGet<int>("no_such_key", out var missingValue);

        Assert.True(found);
        Assert.Equal(5, foundValue);

        Assert.False(typeMismatch);
        Assert.Null(mismatchValue);

        Assert.False(notFound);
        Assert.Equal(0, missingValue);
    }

    [Fact]
    public void Clone_후_원본을_변경해도_복제본은_영향받지_않는다()
    {
        var original = new Msg { Payload = 42, Topic = "sensor/temp" };

        var clone = original.Clone();
        original.Payload = 999; // 원본만 변경

        Assert.Equal(42, clone.Payload);      // 복제본은 여전히 원래 값
        Assert.Equal("sensor/temp", clone.Topic);
        Assert.Equal(999, original.Payload);  // 원본은 변경된 값 그대로
    }

    [Fact]
    public void Clone_은_원본과_동일한_Id를_유지한다()
    {
        // 02번 설계 문서 원본 Clone() 구현을 그대로 따름 — 한 메시지에서 갈라져 나온
        // Fan-out 분기들은 모두 같은 _msgid를 유지해야 Complete 노드 등에서 서로 연관된
        // 메시지임을 추적할 수 있다.
        var original = new Msg();

        var clone = original.Clone();

        Assert.Equal(original.Id, clone.Id);
    }

    [Fact]
    public void Clone_은_동적_필드도_복제한다()
    {
        dynamic original = new Msg();
        original.customField = "hello";

        Msg clone = ((Msg)original).Clone();
        dynamic dynClone = clone;

        Assert.Equal("hello", (string)dynClone.customField);
    }

    [Fact]
    public void ToJson_FromJson_왕복_시_Payload_Topic_값이_보존된다()
    {
        var original = new Msg { Payload = 42, Topic = "sensor/temp" };

        var json = original.ToJson();
        var restored = Msg.FromJson(json);

        Assert.Equal(42L, restored.Payload);   // Newtonsoft.Json은 정수를 long으로 역직렬화
        Assert.Equal("sensor/temp", restored.Topic);
    }

    [Fact]
    public void ToJson_FromJson_왕복_시_동적_필드도_보존된다()
    {
        dynamic original = new Msg();
        original.customField = "hello";
        original.customNumber = 123;

        var json = ((Msg)original).ToJson();
        dynamic restored = Msg.FromJson(json);

        Assert.Equal("hello", (string)restored.customField);
        Assert.Equal(123L, (long)restored.customNumber);
    }

    [Fact]
    public void ToJson_FromJson_왕복_시_Id도_보존된다()
    {
        var original = new Msg();

        var json = original.ToJson();
        var restored = Msg.FromJson(json);

        Assert.Equal(original.Id, restored.Id);
    }
}
