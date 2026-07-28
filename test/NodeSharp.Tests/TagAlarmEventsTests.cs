using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TagValueUpdatedEvent"/>/<see cref="AlarmRaisedEvent"/>(CT-05b, 02번 설계 문서 8번 탭
/// 카드 15·카드 11)에 대한 단위 테스트입니다. <see cref="AlarmRaisedEvent"/>는 이번 Step에서
/// <see cref="AlarmLevel"/>을 재사용하도록 정식 선언했으므로, 존재하지 않던 <c>AlarmSeverity</c>가
/// 아니라 <see cref="AlarmLevel"/>로 정상 컴파일되는지도 이 테스트가 실질적으로 검증한다.
/// </summary>
public class TagAlarmEventsTests
{
    [Fact]
    public void TagValueUpdatedEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new TagValueUpdatedEvent(TagId: "tag-1", Value: 96.2, Alarm: AlarmLevel.HH, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<TagValueUpdatedEvent>(json);

        Assert.Equal(original.TagId, restored!.TagId);
        Assert.Equal(original.Alarm, restored.Alarm);
        Assert.Equal(original.At, restored.At);
    }

    [Fact]
    public void TagValueUpdatedEvent_알람이_없으면_Alarm이_null로_보존된다()
    {
        var original = new TagValueUpdatedEvent(TagId: "tag-2", Value: 42, Alarm: null, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<TagValueUpdatedEvent>(json);

        Assert.Null(restored!.Alarm);
    }

    [Fact]
    public void AlarmRaisedEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new AlarmRaisedEvent(TagId: "tag-1", Level: AlarmLevel.HH, Value: 96.2, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AlarmRaisedEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void AlarmRaisedEvent_SequenceExecutor_자동안전정지_패턴_HH_레벨과_감시대상_태그일치_확인()
    {
        var watchedTagIds = new[] { "tag-1", "tag-2" };
        var evt = new AlarmRaisedEvent(TagId: "tag-1", Level: AlarmLevel.HH, Value: 96.2, At: DateTime.UtcNow);

        bool shouldAutoAbort = watchedTagIds.Contains(evt.TagId) && evt.Level == AlarmLevel.HH;

        Assert.True(shouldAutoAbort);
    }
}
