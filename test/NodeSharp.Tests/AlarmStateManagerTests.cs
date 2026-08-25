using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="AlarmStateManager"/>(ED-D07a, 03번 개발 Step맵 — AlarmStateManager + Ack)에 대한 단위
/// 테스트입니다. 이 Step의 완료 기준("태그 값이 임계값을 넘으면 해당 알람 상태로 전이되고, Ack 수행
/// 시 알람 목록에 확인 표시가 남는지")은 IStructureService·실제 PLC 연결과 무관하게 이 클래스만으로
/// 완전히 검증 가능하도록 설계되어(클래스 문서 참고), 여기서 xUnit만으로 완전히 증명합니다.
/// (ED-D07b) 알람 억제(Shelving) 테스트도 이어서 같은 클래스에 추가했습니다 — 완료 기준의 "사유
/// 필수·8시간 상한·Ack와 State로 구분" 부분은 이 클래스만으로 검증 가능하지만, "UI에서 다르게
/// 표시되는지"는 알람 목록 화면 자체가 아직 없어 범위 밖입니다(AlarmStateManager 클래스 remarks 참고).
/// </summary>
public class AlarmStateManagerTests
{
    /// <summary>발행된 이벤트를 타입별로 그대로 기록만 하는 테스트 전용 <see cref="IEventBus"/>(EventBusAdapter를 거치지 않고 결정적으로 검증하기 위함).</summary>
    private sealed class FakeEventBus : IEventBus
    {
        public List<AlarmRaisedEvent> Raised { get; } = new();
        public List<AlarmClearedEvent> Cleared { get; } = new();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) => throw new NotSupportedException("이 테스트에서는 사용하지 않습니다.");

        public void Publish<TEvent>(TEvent evt)
        {
            switch (evt)
            {
                case AlarmRaisedEvent raised: Raised.Add(raised); break;
                case AlarmClearedEvent cleared: Cleared.Add(cleared); break;
            }
        }
    }

    private static TagRuntimeInfo BuildTag(AlarmRuntimeInfo? alarm) =>
        new("tag-1", "토출압력", "map-1", 0, BufFieldType.FloatLE, Scale: null, Alarm: alarm);

    [Fact]
    public void HH_임계값을_넘으면_활성알람으로_전이되고_AlarmRaisedEvent가_발행된다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: 80, L: null, LL: null));

        manager.Evaluate(tag, 95.0);

        var active = manager.GetActiveAlarm("tag-1");
        Assert.NotNull(active);
        Assert.Equal(AlarmLevel.HH, active!.Level);
        Assert.Equal(95.0, active.Value);
        Assert.Equal(AlarmState.Active, active.State);
        Assert.Single(bus.Raised);
        Assert.Equal("tag-1", bus.Raised[0].TagId);
        Assert.Equal(AlarmLevel.HH, bus.Raised[0].Level);
    }

    [Fact]
    public void 알람이_여전히_활성인_동안_재평가해도_AlarmRaisedEvent가_중복_발행되지_않는다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: 80, L: null, LL: null));

        manager.Evaluate(tag, 95.0);
        manager.Evaluate(tag, 96.0); // 여전히 HH 조건 — 재발행 안 됨(클래스 remarks의 GetOrAdd 의미론)
        manager.Evaluate(tag, 92.0);

        Assert.Single(bus.Raised);
        // 클래스 remarks: 최초 진입 값(95.0)에 머무름 — 이후 재평가로 갱신되지 않음.
        Assert.Equal(95.0, manager.GetActiveAlarm("tag-1")!.Value);
    }

    [Fact]
    public void 정상범위로_복귀하면_활성목록에서_제거되고_AlarmClearedEvent가_발행된다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: 80, L: null, LL: null));

        manager.Evaluate(tag, 95.0);
        manager.Evaluate(tag, 50.0); // 정상 범위로 복귀

        Assert.Null(manager.GetActiveAlarm("tag-1"));
        Assert.Single(bus.Cleared);
        Assert.Equal("tag-1", bus.Cleared[0].TagId);
    }

    [Fact]
    public void 알람조건을_한번도_넘지_않았으면_AlarmClearedEvent를_발행하지_않는다()
    {
        // 활성 알람이 없던 태그는 "해제"할 것도 없으므로 불필요한 이벤트가 나가면 안 된다.
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: 80, L: null, LL: null));

        manager.Evaluate(tag, 50.0);

        Assert.Empty(bus.Cleared);
        Assert.Empty(bus.Raised);
    }

    [Fact]
    public void Alarm이_null인_태그는_어떤_값에도_알람이_발생하지_않는다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(alarm: null);

        manager.Evaluate(tag, 999999.0);

        Assert.Null(manager.GetActiveAlarm("tag-1"));
        Assert.Empty(bus.Raised);
    }

    [Fact]
    public void LL_이하로_내려가면_LL_레벨로_전이된다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: null, H: null, L: 10, LL: 5));

        manager.Evaluate(tag, 3.0);

        Assert.Equal(AlarmLevel.LL, manager.GetActiveAlarm("tag-1")!.Level);
    }

    [Fact]
    public void EQ_NE_특정값_비교가_이산_상태_태그에_동작한다()
    {
        var bus = new FakeEventBus();
        var eqManager = new AlarmStateManager(bus);
        var eqTag = BuildTag(new AlarmRuntimeInfo(HH: null, H: null, L: null, LL: null, EQ: 3, NE: null));
        eqManager.Evaluate(eqTag, 3.0);
        Assert.Equal(AlarmLevel.EQ, eqManager.GetActiveAlarm("tag-1")!.Level);

        var neManager = new AlarmStateManager(bus);
        var neTag = BuildTag(new AlarmRuntimeInfo(HH: null, H: null, L: null, LL: null, EQ: null, NE: 1));
        neManager.Evaluate(neTag, 3.0); // 1이 아니므로 NE 알람

        Assert.Equal(AlarmLevel.NE, neManager.GetActiveAlarm("tag-1")!.Level);
    }

    [Fact]
    public void Acknowledge는_활성알람의_State를_Acknowledged로_바꾸고_목록에는_그대로_남는다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: 80, L: null, LL: null));
        manager.Evaluate(tag, 95.0);

        manager.Acknowledge("tag-1");

        var active = manager.GetActiveAlarm("tag-1");
        Assert.NotNull(active);
        Assert.Equal(AlarmState.Acknowledged, active!.State);
    }

    [Fact]
    public void Acknowledge는_활성알람이_없는_태그에_대해_KeyNotFoundException을_던진다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Throws<KeyNotFoundException>(() => manager.Acknowledge("존재하지-않는-태그"));
    }

    [Fact]
    public void GetActiveAlarm은_활성알람이_없는_태그에_대해_null을_반환한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Null(manager.GetActiveAlarm("존재하지-않는-태그"));
    }

    [Fact]
    public void ActiveAlarms는_현재_활성인_모든_알람의_스냅샷을_반환한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());
        manager.Evaluate(new TagRuntimeInfo("tag-1", "A", "map-1", 0, BufFieldType.FloatLE, null,
            new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null)), 95.0);
        manager.Evaluate(new TagRuntimeInfo("tag-2", "B", "map-1", 4, BufFieldType.FloatLE, null,
            new AlarmRuntimeInfo(HH: 50, H: null, L: null, LL: null)), 60.0);

        var snapshot = manager.ActiveAlarms;

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, a => a.TagId == "tag-1");
        Assert.Contains(snapshot, a => a.TagId == "tag-2");
    }

    // ── ED-D07b: 알람 억제(Shelving) ──────────────────────────────────────────

    [Fact]
    public void Shelve는_사유가_비어있으면_ArgumentException을_던진다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Throws<ArgumentException>(() => manager.Shelve("tag-1", TimeSpan.FromHours(1), reason: "", user: "operator1"));
        Assert.Throws<ArgumentException>(() => manager.Shelve("tag-1", TimeSpan.FromHours(1), reason: "   ", user: "operator1"));
    }

    [Fact]
    public void Shelve는_기간이_0이하이면_ArgumentOutOfRangeException을_던진다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Shelve("tag-1", TimeSpan.Zero, "정비 예정", "operator1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Shelve("tag-1", TimeSpan.FromMinutes(-1), "정비 예정", "operator1"));
    }

    [Fact]
    public void Shelve는_8시간을_초과하면_ArgumentOutOfRangeException을_던져_무기한_억제를_막는다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.Shelve("tag-1", TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1), "정비 예정", "operator1"));
    }

    [Fact]
    public void Shelve는_정확히_8시간이면_허용한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        var ex = Record.Exception(() => manager.Shelve("tag-1", AlarmStateManager.MaxShelveDuration, "정비 예정", "operator1"));

        Assert.Null(ex);
    }

    [Fact]
    public void 억제_중인_태그는_알람조건을_만족해도_Shelved로_표시되고_AlarmRaisedEvent가_발행되지_않는다()
    {
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null));

        manager.Shelve("tag-1", TimeSpan.FromHours(2), "센서 노후화로 재점검 예정", "operator1", now);
        manager.Evaluate(tag, 95.0, now.AddMinutes(1));

        var active = manager.GetActiveAlarm("tag-1");
        Assert.NotNull(active);
        Assert.Equal(AlarmState.Shelved, active!.State);
        Assert.Empty(bus.Raised);
    }

    [Fact]
    public void 억제_중이어도_정상범위로_복귀하면_해제되고_AlarmClearedEvent가_발행된다()
    {
        // 클래스 remarks 판단 근거 ① — 억제는 "재발행 억제"이지 "해제 억제"가 아니다.
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null));

        manager.Shelve("tag-1", TimeSpan.FromHours(2), "정비 예정", "operator1", now);
        manager.Evaluate(tag, 95.0, now.AddMinutes(1)); // 억제 중 알람 발생 — Shelved
        manager.Evaluate(tag, 10.0, now.AddMinutes(2)); // 정상 범위로 복귀

        Assert.Null(manager.GetActiveAlarm("tag-1"));
        Assert.Single(bus.Cleared);
    }

    [Fact]
    public void 억제기간이_지나면_재평가시_재발행없이_Active로_복귀한다()
    {
        // 클래스 remarks 판단 근거 ③ — 계속되던 같은 알람이므로 억제가 풀려도 새 AlarmRaisedEvent는 없음.
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null));

        manager.Shelve("tag-1", TimeSpan.FromMinutes(30), "정비 예정", "operator1", now);
        manager.Evaluate(tag, 95.0, now.AddMinutes(1)); // 억제 중 — Shelved
        manager.Evaluate(tag, 96.0, now.AddHours(1)); // 억제 기간(30분) 경과 후 재평가 — 여전히 알람 조건

        var active = manager.GetActiveAlarm("tag-1");
        Assert.Equal(AlarmState.Active, active!.State);
        Assert.Empty(bus.Raised); // 계속되던 같은 알람이므로 재발행 없음
    }

    [Fact]
    public void 이미_Active이거나_Acknowledged인_알람도_Shelve_호출후_재평가하면_Shelved로_전환된다()
    {
        // "Ack=1회성, Shelve=당분간 억제"라는 정책 문구 — Shelve가 Ack보다 우선해 표시를 덮어쓴다.
        var bus = new FakeEventBus();
        var manager = new AlarmStateManager(bus);
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null));

        manager.Evaluate(tag, 95.0, now); // Active
        manager.Acknowledge("tag-1"); // Acknowledged
        manager.Shelve("tag-1", TimeSpan.FromHours(1), "정비 예정", "operator1", now.AddMinutes(1));
        manager.Evaluate(tag, 96.0, now.AddMinutes(2)); // 재평가 — Shelved로 전환

        Assert.Equal(AlarmState.Shelved, manager.GetActiveAlarm("tag-1")!.State);
    }

    [Fact]
    public void IsShelved는_억제_기간_중에는_true_지나면_false를_반환한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);

        manager.Shelve("tag-1", TimeSpan.FromHours(1), "정비 예정", "operator1", now);

        Assert.True(manager.IsShelved("tag-1", now.AddMinutes(30)));
        Assert.False(manager.IsShelved("tag-1", now.AddHours(2)));
    }

    [Fact]
    public void GetShelveInfo는_사유_요청자_해제시각을_그대로_반환한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);

        manager.Shelve("tag-1", TimeSpan.FromHours(3), "센서 노후화로 재점검 예정", "operator1", now);
        var info = manager.GetShelveInfo("tag-1", now.AddMinutes(5));

        Assert.NotNull(info);
        Assert.Equal("센서 노후화로 재점검 예정", info!.Reason);
        Assert.Equal("operator1", info.User);
        Assert.Equal(now + TimeSpan.FromHours(3), info.Until);
    }

    [Fact]
    public void GetShelveInfo는_억제_중이_아니면_null을_반환한다()
    {
        var manager = new AlarmStateManager(new FakeEventBus());

        Assert.Null(manager.GetShelveInfo("존재하지-않는-태그"));
    }

    [Fact]
    public void Ack와_Shelve는_ActiveAlarm의_State가_서로_다른_값으로_구분된다()
    {
        // 완료 기준: "Ack·Shelve가 UI에서 다르게 표시되는지" — 이 값(State)이 그 근거 데이터.
        // 실제 UI 표시(뱃지 등)는 알람 목록 화면이 아직 없어(DB-01a~f 등 ⏳ 대기) 범위 밖(클래스 remarks 참고).
        var bus = new FakeEventBus();
        var ackManager = new AlarmStateManager(bus);
        var now = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var tag = BuildTag(new AlarmRuntimeInfo(HH: 90, H: null, L: null, LL: null));
        ackManager.Evaluate(tag, 95.0, now);
        ackManager.Acknowledge("tag-1");

        var shelveManager = new AlarmStateManager(bus);
        shelveManager.Shelve("tag-1", TimeSpan.FromHours(1), "정비 예정", "operator1", now);
        shelveManager.Evaluate(tag, 95.0, now.AddMinutes(1));

        Assert.Equal(AlarmState.Acknowledged, ackManager.GetActiveAlarm("tag-1")!.State);
        Assert.Equal(AlarmState.Shelved, shelveManager.GetActiveAlarm("tag-1")!.State);
        Assert.NotEqual(ackManager.GetActiveAlarm("tag-1")!.State, shelveManager.GetActiveAlarm("tag-1")!.State);
    }
}
