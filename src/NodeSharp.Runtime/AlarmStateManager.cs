using System.Collections.Concurrent;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 알람 확인 상태
/// 역활 및 기능 : 활성 알람 1건이 운영자에게 확인(Acknowledge)됐는지를 나타내는 상태
///
/// (ED-D07a) 02번 설계문서 8번 탭 카드11 원본은 <c>enum AlarmState { Active, Acknowledged, Cleared }</c>
/// 3개 값을 선언하지만, 원본 알고리즘 자체는 <c>Cleared</c>를 한 번도 실제로 대입하지 않습니다 — 값이
/// 정상 범위로 돌아오면 활성 알람 목록에서 항목 자체를 제거할 뿐(<see cref="AlarmStateManager.Evaluate"/>),
/// "Cleared 상태로 남겨두는" 개념이 코드에 없습니다(제거된 항목은 더 이상 <c>ActiveAlarms</c>에 존재하지
/// 않으므로 상태를 가질 수 없음). 이 Step의 완료 기준도 "임계값을 넘으면 전이·Ack 시 표시"만 요구해
/// Cleared 상태 자체를 쓸 곳이 없으므로, 사용하지 않는 값을 두지 않고 실제로 대입되는 2개 값만
/// 선언했습니다(ED-D02a "그룹"·ED-D04 "팝업"과 동일한 유형의 문서-코드 불일치 해소 판단).
/// (ED-D07b) 알람 억제(Shelving) 구현 시 <see cref="Shelved"/> 값을 추가했습니다 — 이번엔 원본 카드11의
/// 억제 관련 주석("ActiveAlarm 목록에는 '억제됨' 상태로 표시 — 완전히 숨기지는 않음")이 실제로
/// 요구하는 상태라 Cleared와 달리 실제로 대입됩니다(<see cref="AlarmStateManager.Evaluate"/> 참고).
/// </summary>
public enum AlarmState
{
    /// <summary>알람이 발생해 활성 목록에 있고, 아직 운영자가 확인하지 않았습니다.</summary>
    Active,

    /// <summary>운영자가 확인(Acknowledge)했지만, 값이 아직 정상 범위로 돌아오지 않아 활성 목록에는 남아 있습니다.</summary>
    Acknowledged,

    /// <summary>(ED-D07b) 운영자가 <see cref="AlarmStateManager.Shelve"/>로 억제해, 억제 기간 동안은 재발행(<see cref="AlarmRaisedEvent"/>)이 나가지 않지만 목록에는 계속 표시됩니다.</summary>
    Shelved,
}

/// <summary>
/// Class명 : 알람 억제 정보
/// 역활 및 기능 : 태그 1건에 대한 억제(Shelve) 사유·요청자·해제 시각을 담는 데이터
///
/// (ED-D07b) 02번 설계문서 8번 탭 카드11의 <c>Shelve(tagId, duration, reason, user)</c> 원본 스니펫이
/// 남긴 주석("7번 탭 AuditEntry에 'AlarmShelve' 기록, Detail에 reason 포함")은 <c>AuditEntry</c> 타입
/// 자체가 이 프로젝트에 아직 없어(<c>DeviceMapPoller</c>의 <c>AlarmStateManager</c>·<c>ITagHistorian</c>과
/// 동일한 유형의 미구현 의존성) 이 Step 범위 밖입니다 — 대신 <see cref="Reason"/>/<see cref="User"/>를
/// 이 레코드에 그대로 보존해둬, <c>AuditEntry</c>가 생기는 후속 Step이 이 값을 그대로 옮겨 기록할 수
/// 있게 했습니다.
/// </summary>
/// <param name="Reason">억제 사유(필수 — <see cref="AlarmStateManager.Shelve"/>가 빈 값을 거부합니다).</param>
/// <param name="User">억제를 요청한 사용자.</param>
/// <param name="Until">억제가 해제되는 시각(UTC) — <see cref="AlarmStateManager.Shelve"/>가 호출 시각 + duration으로 계산합니다.</param>
public sealed record ShelveInfo(string Reason, string User, DateTime Until);

/// <summary>
/// Class명 : 활성 알람 항목
/// 역활 및 기능 : 현재 활성 상태인 알람 1건을 나타내는 데이터
/// </summary>
/// <param name="TagId">이 알람을 일으킨 태그의 Id.</param>
/// <param name="Level">알람이 발생한 등급(HH/H/L/LL/EQ/NE).</param>
/// <param name="Value">알람이 처음 발생한 시점의 태그 값.</param>
/// <param name="RaisedAt">알람이 처음 발생한 시각(UTC).</param>
/// <param name="State">현재 확인 상태.</param>
public sealed record ActiveAlarm(string TagId, AlarmLevel Level, double Value, DateTime RaisedAt, AlarmState State);

/// <summary>
/// Class명 : 알람 상태 관리자
/// 역활 및 기능 : 태그 값 갱신마다 임계값/특정값을 검사해 활성 알람 목록을 관리하고 Ack를 처리하는 런타임 매니저
///
/// (ED-D07a) 02번 설계문서 8번 탭 카드11(알람 런타임 관리) 원본 스니펫을 이 프로젝트 실제 아키텍처에
/// 맞게 구현했습니다. 원본은 <c>Evaluate(TagNode tag, AlarmNode rule, double value)</c>로 Editor 전용
/// 트리 타입을 직접 받지만, <see cref="DeviceMapPoller"/>(ED-D06b)와 동일하게 순수 데이터인
/// <see cref="TagRuntimeInfo"/>(<c>CT-03b</c>, <c>Alarm</c> 필드에 이미 HH/H/L/LL/EQ/NE 6종 비교값을
/// 담고 있음)를 받도록 바꿔 <c>TagNode</c>/<c>AlarmNode</c> 없이도 헤드리스로 동작합니다. 이 Step의
/// 완료 기준("태그 값이 임계값을 넘으면 해당 알람 상태로 전이되고, Ack 수행 시 알람 목록에 확인 표시가
/// 남는지")은 <c>IStructureService</c>·실제 PLC 연결과 무관하게 이 클래스만으로 완전히 검증 가능합니다
/// — <c>DeviceMapPoller</c>/<c>PlcTagReadNode</c>와 달리 이 Step은 처음부터 외부 인프라 의존이 없습니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>레벨 판정 순서</b>: 원본 스니펫의 실제 삼항 연산자 체인은 HH→H→LL→L→EQ→NE 순서로 검사합니다
/// (원본 설명 문구는 "HH→H→L→LL→EQ→NE"라고 서술하지만, 실제 코드는 LL을 L보다 먼저 검사함 — 코드와
/// 설명이 다를 때는 실제 동작인 코드를 기준으로 삼는다는 이 프로젝트의 기존 원칙에 따라 코드 순서를
/// 그대로 재현했습니다). 어차피 한 태그에 HH/H와 LL/L이 동시에 조건을 만족하는 값은 있을 수 없어(예:
/// 값이 90이면 HH=90 조건은 만족해도 LL=10 조건은 만족 못함) 실제 판정 결과에는 영향이 없습니다.</item>
/// <item><b>활성 알람은 최초 진입 값에서 갱신되지 않음(GetOrAdd 의미론 그대로 재현)</b>: 원본이
/// <c>_active.GetOrAdd(key, _ =&gt; new ActiveAlarm(...))</c>를 쓰는 그대로, 이미 활성 상태인 태그가
/// 값만 바뀌고 계속 알람 조건 안에 있으면(예: HH였다가 H로 낮아짐) <see cref="ActiveAlarm.Value"/>/
/// <see cref="ActiveAlarm.Level"/>은 최초 진입 시점 값에 머무릅니다 — 값이 정상 범위로 돌아와 완전히
/// 해제된 뒤 다시 조건을 만족해야 새 <see cref="ActiveAlarm"/>으로 갱신됩니다. 원본 카드 자체가 남긴
/// "발견한 공백(연동)" 메모(Ack는 지금 뜬 알람 하나만 조용히 시키고, 완전히 해제됐다 다시 발생하면
/// 다시 울린다)와 일치하는 의도된 동작이며, 값이 계속 바뀌는데도 알람 목록 값이 고정돼 보이는 것을
/// "레벨 승격/강등"까지 실시간 반영하는 것은 이 Step 범위 밖(완료 기준에 없음)입니다.</item>
/// <item><b><see cref="IEventBus"/> 기본값</b>: 생성자에 넘기지 않으면 <c>EventBusAdapter</c>(앱 전체
/// 공유 <c>EventBus.Instance</c>)를 기본으로 씁니다 — <c>InjectNode.Scheduler</c>와 동일한 관례. 테스트는
/// 독립된 페이크를 주입해 발행된 이벤트를 직접 검증합니다.</item>
/// <item><b>(ED-D07b) 억제(Shelve) 동안의 동작 — 판단 근거</b>: 원본 카드11은 <c>Shelve</c>를 코드가
/// 아니라 주석으로만 서술해("Evaluate()는 이 메서드 시작부에서 ... 임계값 초과여도 AlarmRaisedEvent를
/// 발행하지 않고 조용히 리턴, 단 ActiveAlarm 목록에는 '억제됨' 상태로 표시") 실제 상태 전이 규칙까지
/// 정하지는 않았습니다. 이 구현은 다음 원칙으로 판단해 채웠습니다: ①억제는 "재발행(RaisedEvent) 억제"
/// 이지 "해제(ClearedEvent) 억제"가 아니므로, 값이 정상 범위로 돌아오면 억제 중이어도 평소처럼
/// 목록에서 제거되고 <see cref="AlarmClearedEvent"/>가 발행됩니다. ②억제 중 알람 조건을 계속/새로
/// 만족하면 <see cref="ActiveAlarm.State"/>가 <see cref="AlarmState.Shelved"/>로 표시되고
/// <see cref="AlarmRaisedEvent"/>는 나가지 않습니다(이미 <see cref="AlarmState.Active"/>·
/// <see cref="AlarmState.Acknowledged"/>였더라도 Shelve 호출 이후 재평가되면 Shelved로 전환됩니다 —
/// "Ack=1회성, Shelve=당분간 억제"라는 원본 카드11의 정책 문구와 일치). ③억제 기간이 지나면 다음
/// <see cref="Evaluate"/> 호출부터 평소처럼 동작하되, 여전히 알람 조건이면 새 <see cref="AlarmRaisedEvent"/>
/// 없이(계속되던 같은 알람이므로) <see cref="AlarmState.Active"/>로만 전환됩니다.</item>
/// <item><b>(ED-D07b) 완료 기준의 UI 절반은 범위 밖</b>: 이 Step 완료 기준은 "Shelve 설정 시 사유 입력이
/// 필수이고 8시간 초과 억제는 거부"(이 클래스만으로 완전히 검증 가능)와 "Ack·Shelve가 UI에서 다르게
/// 표시되는지"(알람 목록을 보여줄 화면 자체가 필요) 두 갈래인데, 이 프로젝트에는 아직 알람 목록을
/// 표시하는 화면이 어디에도 없습니다(<c>DB-01a~f</c>·<c>ED-D11a/b</c> 모두 <c>⏳ 대기</c>로 확인) —
/// <c>ED-D05b</c>가 OP-04 부재로 부분 보류됐던 것과 동일한 유형의 공백입니다. 이 Step은 완전히
/// 건너뛰지 않고(UI 절반만 없을 뿐 나머지 절반은 지금 완전히 구현·검증 가능하므로) 검증 가능한 절반
/// (사유 필수·8시간 상한·Ack와 다른 State로 전이)만 먼저 구현하고, UI 표시 확인은 해당 화면이 생기는
/// 후속 Step(<c>DB-01a~f</c>류) 이후로 미룹니다.</item>
/// </list>
/// </remarks>
public sealed class AlarmStateManager
{
    private readonly ConcurrentDictionary<string, ActiveAlarm> _active = new();
    private readonly ConcurrentDictionary<string, ShelveInfo> _shelved = new();
    private readonly IEventBus _eventBus;

    /// <summary>(ED-D07b) 완료 기준이 요구하는 "8시간 초과 억제는 거부"의 상한값입니다 — 무기한 억제 방치를 막기 위한 운영 정책(02번 문서 8번 탭 카드11).</summary>
    public static readonly TimeSpan MaxShelveDuration = TimeSpan.FromHours(8);

    /// <summary>(InjectNode.Scheduler와 동일한 관례) <paramref name="eventBus"/>를 생략하면 앱 전체가 공유하는 기본 <c>EventBusAdapter</c>를 사용합니다.</summary>
    public AlarmStateManager(IEventBus? eventBus = null) => _eventBus = eventBus ?? new EventBusAdapter();

    /// <summary>현재 활성 상태인 모든 알람의 스냅샷을 반환합니다(테스트·화면 표시용, 내부 상태를 직접 노출하지 않음).</summary>
    public IReadOnlyList<ActiveAlarm> ActiveAlarms => _active.Values.ToList();

    /// <summary>지정한 태그의 활성 알람을 반환합니다. 활성 알람이 없으면 <c>null</c>입니다.</summary>
    public ActiveAlarm? GetActiveAlarm(string tagId) => _active.TryGetValue(tagId, out var alarm) ? alarm : null;

    /// <summary>
    /// <paramref name="tag"/>.Alarm의 HH/H/L/LL/EQ/NE 기준으로 <paramref name="value"/>를 검사합니다.
    /// 어떤 조건도 만족하지 않으면(또는 <c>tag.Alarm</c>이 <c>null</c>이면) 기존 활성 알람이 있었을
    /// 경우에만 제거하고 <see cref="AlarmClearedEvent"/>를 발행합니다(★ ED-D07b: 억제 중이어도 해제는
    /// 그대로 적용 — 클래스 remarks 참고). 조건을 만족하고 현재 억제(<see cref="Shelve"/>) 중이 아니면
    /// 해당 태그가 처음 활성화되는 경우에만 새 <see cref="ActiveAlarm"/>을 만들고
    /// <see cref="AlarmRaisedEvent"/>를 발행합니다(클래스 remarks의 "활성 알람은 최초 진입 값에서
    /// 갱신되지 않음" 참고 — 같은 알람의 반복 발행을 막습니다). 조건을 만족하는데 현재 억제 중이면
    /// (★ ED-D07b) <see cref="AlarmState.Shelved"/>로만 표시하고 <see cref="AlarmRaisedEvent"/>는
    /// 발행하지 않습니다.
    /// </summary>
    public void Evaluate(TagRuntimeInfo tag, double value, DateTime? at = null)
    {
        var timestamp = at ?? DateTime.UtcNow;
        var level = DetermineLevel(tag.Alarm, value);

        if (level is null)
        {
            if (_active.TryRemove(tag.Id, out _))
            {
                _eventBus.Publish(new AlarmClearedEvent(tag.Id, timestamp));
            }

            return;
        }

        if (IsShelved(tag.Id, timestamp))
        {
            // (ED-D07b) 억제 중 — 재발행은 막되 목록에는 계속 표시(클래스 remarks 판단 근거 ② 참고).
            var raisedAt = _active.TryGetValue(tag.Id, out var existingShelved) ? existingShelved.RaisedAt : timestamp;
            _active[tag.Id] = new ActiveAlarm(tag.Id, level.Value, value, raisedAt, AlarmState.Shelved);
            return;
        }

        var isNew = false;
        _active.AddOrUpdate(
            tag.Id,
            _ =>
            {
                isNew = true;
                return new ActiveAlarm(tag.Id, level.Value, value, timestamp, AlarmState.Active);
            },
            (_, existing) => existing.State == AlarmState.Shelved
                ? existing with { State = AlarmState.Active } // (ED-D07b) 억제 기간이 지남 — 계속되던 알람이므로 재발행 없이 Active로만 복귀(판단 근거 ③).
                : existing);

        if (isNew)
        {
            _eventBus.Publish(new AlarmRaisedEvent(tag.Id, level.Value, value, timestamp));
        }
    }

    /// <summary>
    /// 지정한 태그의 활성 알람을 확인(Acknowledge) 상태로 전환합니다 — 목록에는 그대로 남고
    /// <see cref="ActiveAlarm.State"/>만 <see cref="AlarmState.Acknowledged"/>로 바뀝니다. 활성 알람이
    /// 없는 태그를 확인하려 하면 <see cref="KeyNotFoundException"/>을 던집니다(원본 스니펫과 동일).
    /// </summary>
    public void Acknowledge(string tagId)
    {
        _active.AddOrUpdate(
            tagId,
            _ => throw new KeyNotFoundException($"활성 알람이 없어 확인(Ack)할 수 없습니다: {tagId}"),
            (_, existing) => existing with { State = AlarmState.Acknowledged });
    }

    /// <summary>
    /// (ED-D07b) 지정한 태그를 <paramref name="duration"/> 동안 억제(Shelve)합니다 — 억제 기간 동안은
    /// <see cref="Evaluate"/>가 <see cref="AlarmRaisedEvent"/>를 재발행하지 않고 <see cref="AlarmState.Shelved"/>로만
    /// 표시합니다(완전히 숨기지 않음 — 클래스 remarks 참고). 완료 기준이 요구하는 2가지 유효성 검사를
    /// 여기서 수행합니다: <paramref name="reason"/>이 비어 있으면 <see cref="ArgumentException"/>,
    /// <paramref name="duration"/>이 0 이하이거나 <see cref="MaxShelveDuration"/>(8시간)을 넘으면
    /// <see cref="ArgumentOutOfRangeException"/>을 던져 무기한 억제를 방지합니다.
    /// </summary>
    public void Shelve(string tagId, TimeSpan duration, string reason, string user, DateTime? at = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("억제 사유(reason)는 필수입니다 — 무기한 억제 방치를 막기 위한 정책입니다.", nameof(reason));
        }

        if (duration <= TimeSpan.Zero || duration > MaxShelveDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                duration, $"억제 시간은 0보다 크고 최대 {MaxShelveDuration}(8시간)을 넘을 수 없습니다(무기한 억제 금지).");
        }

        var timestamp = at ?? DateTime.UtcNow;
        _shelved[tagId] = new ShelveInfo(reason, user, timestamp + duration);
        // (ED-D07b) 감사 로그(AuditEntry) 기록은 그 타입이 아직 없어 범위 밖입니다 — ShelveInfo 클래스
        // 문서 참고, reason/user는 이 딕셔너리에 보존되어 후속 Step이 그대로 옮겨 기록할 수 있습니다.
    }

    /// <summary>지정한 태그가 현재 억제 중인지 확인합니다. 억제 기간이 이미 지났으면 내부 기록을 정리하고 <c>false</c>를 반환합니다.</summary>
    public bool IsShelved(string tagId, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        if (_shelved.TryGetValue(tagId, out var info))
        {
            if (info.Until > now)
            {
                return true;
            }

            _shelved.TryRemove(tagId, out _); // 만료된 억제 기록 정리.
        }

        return false;
    }

    /// <summary>지정한 태그의 억제 정보(사유·요청자·해제 시각)를 반환합니다. 억제 중이 아니면(또는 만료됐으면) <c>null</c>입니다.</summary>
    public ShelveInfo? GetShelveInfo(string tagId, DateTime? at = null) =>
        IsShelved(tagId, at) && _shelved.TryGetValue(tagId, out var info) ? info : null;

    /// <summary>원본 스니펫의 실제 삼항 연산자 순서(HH→H→LL→L→EQ→NE)를 그대로 재현합니다 — 클래스 remarks 참고.</summary>
    private static AlarmLevel? DetermineLevel(AlarmRuntimeInfo? rule, double value)
    {
        if (rule is null)
        {
            return null;
        }

        if (rule.HH is double hh && value >= hh) return AlarmLevel.HH;
        if (rule.H is double h && value >= h) return AlarmLevel.H;
        if (rule.LL is double ll && value <= ll) return AlarmLevel.LL;
        if (rule.L is double l && value <= l) return AlarmLevel.L;
        if (rule.EQ is double eq && value == eq) return AlarmLevel.EQ;
        if (rule.NE is double ne && value != ne) return AlarmLevel.NE;

        return null;
    }
}
