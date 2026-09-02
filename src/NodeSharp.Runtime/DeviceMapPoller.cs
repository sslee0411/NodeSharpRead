using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Util.Messaging;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 디바이스맵 배치 폴링 엔진
/// 역활 및 기능 : 디바이스맵(블록) 하나를 주기적으로 통째로 읽어 그 안의 모든 태그 값을 한 번에 캐시에 갱신하는 공유 서비스
///
/// (ED-D06b) 02번 설계문서 8번 탭 카드9(DeviceMap 배치 폴링 엔진 + Tag 값 캐시) 원본 스니펫을 이
/// 프로젝트 실제 아키텍처에 맞게 구현했습니다. 원본은 <c>IStructureService _structure</c>(태그 목록
/// 조회·스케일 변환)·<c>INetTransportProvider/_netTransport</c>(실제 PLC 통신)·
/// <c>AlarmStateManager _alarmManager</c>·<c>ITagHistorian _historian</c> 4개를 직접 참조하지만,
/// 이 시점에 전부 인터페이스만 있거나(<c>IStructureService</c>, CT-04b) 아예 존재하지 않습니다
/// (<c>INetTransportProvider</c>는 LL-05a 대기, <c>AlarmStateManager</c>는 ED-D07a, <c>ITagHistorian</c>류는
/// ED-D08a — 전부 아직 <c>⏳ 대기</c>). 이 Step의 완료 기준("태그 10개 이상을 배치 폴링으로 묶으면
/// <see cref="TagValueCache"/>를 경유해 개별 폴링 대비 통신 횟수가 줄어드는지")은 그 4개 의존성과
/// 무관하게 검증 가능하므로, <c>PlcTagReadNode</c>(ED-D04)·<c>PlcTagWriteNode</c>(ED-D06a)와
/// 동일한 범위 축소 원칙으로 진행했습니다:
/// <list type="bullet">
/// <item><b><see cref="TagIds"/></b> — <c>IStructureService.GetTagsByMap</c> 대신, 이 디바이스맵에 속한
/// 태그 Id 목록을 생성자/초기화 시점에 그대로 받습니다(태그 목록 자체는 배포마다 고정된 순수 데이터라,
/// 서비스를 거치지 않고 주입해도 이 Step의 완료 기준을 충족하는 데 문제가 없습니다).</item>
/// <item><b><see cref="BlockReadAction"/></b> — <c>_netTransport.ReadAsync</c> + <c>BufferParser.Parse</c>
/// 조합(실제 PLC에서 블록 전체를 1회 읽고 태그별로 분해하는 부분)을 테스트 주입 가능한 델리게이트
/// 하나로 대체했습니다(<c>InjectNode.Scheduler</c>·<c>PlcTagWriteNode.WriteAction</c>과 동일한
/// 관례) — 기본값 <c>null</c>은 아무 것도 하지 않는 자리표시자입니다.</item>
/// <item><b>AlarmStateManager·ITagHistorian 연동은 범위 밖</b> — 원본 스니펫의 알람 평가·이력 기록
/// 호출은 이 Step의 완료 기준에 없고, 그 대상 클래스 자체가 아직 없어 후속 Step(ED-D07a/ED-D08a)에서
/// 이 클래스에 이어 붙일 예정입니다.</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="Scheduler"/>는 <c>InjectNode</c>(NR-03b)와 동일한 관례로 <c>null</c>이면
/// <see cref="OnStartAsync"/>가 기본값으로 앱 전체가 공유하는 <see cref="AsyncSchedulerAdapter"/>를
/// 직접 생성합니다 — 테스트는 독립된 페이크로 교체해 실제 시간 경과 없이 결정적으로 검증합니다.
/// <see cref="PollOnceAsync"/>는 <see cref="OnStartAsync"/>가 등록하는 주기 콜백이 실제로 호출하는
/// 로직이지만, 테스트가 스케줄러를 거치지 않고 직접 호출해 "배치 1회 읽기로 여러 태그가 한 번에
/// 갱신되는지"를 곧바로 증명할 수 있도록 공개 메서드로 두었습니다.
/// </remarks>
public sealed class DeviceMapPoller : ISharedServiceNode
{
    /// <inheritdoc />
    /// <remarks>디바이스맵 Id 기준입니다 — 같은 디바이스맵을 가리키는 폴러는 항상 같은 Id를 가져야 합니다(<see cref="ISharedServiceNode.Id"/> 문서 참고).</remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>(ED-D06b) 이 디바이스맵에 속한 태그 Id 목록 — 클래스 remarks의 "TagIds" 항목 참고.</summary>
    public IReadOnlyList<string> TagIds { get; init; } = Array.Empty<string>();

    /// <summary>배치로 읽은 태그 값을 갱신해 넣을 공유 캐시입니다. 여러 DeviceMapPoller가 같은 인스턴스를 공유할 수 있습니다.</summary>
    public TagValueCache Cache { get; init; } = new();

    /// <summary>배치 폴링 주기입니다. 기본값 1초.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>(NR-03b InjectNode.Scheduler와 동일한 관례) 지정하지 않으면 <see cref="OnStartAsync"/>가 기본 <see cref="AsyncSchedulerAdapter"/>를 직접 생성합니다.</summary>
    public IScheduler? Scheduler { get; set; }

    /// <summary>
    /// (ED-D06b) 실제 블록 읽기 동작 — 기본값(<c>null</c>)은 아무 것도 하지 않는 자리표시자입니다
    /// (클래스 문서 참고, 실제 PLC 통신은 IStructureService/INetTransportProvider가 갖춰진 후속 Step으로
    /// 미룹니다). 한 번 호출될 때마다 이 디바이스맵에 속한 태그들의 "현재 값"을 태그 Id 기준으로 묶어
    /// 반환해야 합니다(원본 스니펫의 "블록 전체 1회 읽기 + BufferParser로 분해"를 하나로 합친 동작).
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyDictionary<string, object?>>>? BlockReadAction { get; set; }

    /// <summary>
    /// (PD-01e, ★ 신규) 지정하면 <see cref="PollOnceAsync"/>가 값이 실제로 바뀐 태그마다
    /// <see cref="TagValueUpdatedEvent"/>를 이 버스에 발행합니다(<see cref="TagAlarmEvents"/>의
    /// "DeviceMapPoller가 폴링 캐시 갱신 직후 이전 값과 다를 때만 발행" 클래스 문서 그대로 구현) —
    /// 생략(기본값 <c>null</c>)하면 <see cref="Cache"/> 갱신만 하고 아무 것도 발행하지 않습니다(기존
    /// ED-D06b 테스트·호출부와 완전히 동일하게 동작, 하위 호환).
    /// </summary>
    public IEventBus? EventBus { get; init; }

    private IScheduler? _activeScheduler;

    /// <summary>
    /// <see cref="Scheduler"/>(없으면 기본 <see cref="AsyncSchedulerAdapter"/>)에 <see cref="Id"/>를
    /// ownerId 삼아 <see cref="PollOnceAsync"/>를 <see cref="PollInterval"/>마다 반복 실행하도록 등록합니다.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        _activeScheduler = Scheduler ?? new AsyncSchedulerAdapter();
        _activeScheduler.SchedulePeriodic(Id, PollInterval, () => PollOnceAsync(ct));
        return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="BlockReadAction"/>을 1회 호출해(태그 개수와 무관하게 통신 1회) 반환된 값 중
    /// <see cref="TagIds"/>에 속한 것만 <see cref="Cache"/>에 반영합니다. <see cref="BlockReadAction"/>이
    /// <c>null</c>이면 아무 것도 하지 않습니다. 결과 딕셔너리에 없는 태그는 이전 캐시 값을 그대로
    /// 유지합니다(부분 실패로 캐시가 비워지지 않도록).
    /// </summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        if (BlockReadAction is null)
        {
            return;
        }

        var values = await BlockReadAction(ct).ConfigureAwait(false);
        foreach (var tagId in TagIds)
        {
            if (!values.TryGetValue(tagId, out var value))
            {
                continue;
            }

            // (PD-01e) EventBus가 있을 때만 "이전 값과 다른지" 비교한다 — 없으면(기존 ED-D06b 호출부)
            // TryGetCached를 호출할 이유가 없어 예전과 동일하게 Cache.Set만 수행(불필요한 오버헤드 없음).
            if (EventBus is not null)
            {
                var changed = !Cache.TryGetCached(tagId, out var previous) || !Equals(previous, value);
                Cache.Set(tagId, value);
                if (changed)
                {
                    EventBus.Publish(new TagValueUpdatedEvent(tagId, value, Alarm: null, DateTime.UtcNow));
                }
            }
            else
            {
                Cache.Set(tagId, value);
            }
        }
    }

    /// <summary>지정한 태그의 캐시된 최신값을 즉시 반환합니다(PLC 재통신 없음) — 원본 스니펫의 <c>GetCached</c>와 동일합니다.</summary>
    public object? GetCached(string tagId) => Cache.GetCached(tagId);

    /// <inheritdoc />
    public Task StopAsync()
    {
        _activeScheduler?.Unschedule(Id);
        _activeScheduler = null;
        return Task.CompletedTask;
    }
}
