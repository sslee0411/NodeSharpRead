using NodeSharp.Contracts.Interfaces;
using NodeSharp.Util.Messaging;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 비동기 스케줄러 어댑터
/// 역활 및 기능 : IScheduler 계약을 포팅된 AsyncScheduler로 구현하는 어댑터
///
/// <see cref="IScheduler"/>(Contracts 계약)를 <see cref="AsyncScheduler"/>(NodeSharp.Util로 포팅된
/// lssLib.Messaging.AsyncScheduler)로 구현하는 어댑터입니다. <see cref="EventBusAdapter"/>와 같은 역할 —
/// Contracts는 구체 타입을 몰라야 하므로 이 어댑터가 그 둘을 이어줍니다.
/// 설계 근거: 02번 문서 6번 탭 카드5(<c>IScheduler</c> 계약), dev-csharp 스킬 lssLib.Messaging 문서(원본
/// <c>AsyncScheduler</c> 동작).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><see cref="Unschedule"/>은 직접 구현이 필요함</b> — 포팅한 <see cref="AsyncScheduler"/>는
/// "이름으로 취소"하는 메서드가 없고, <see cref="AsyncScheduler.Schedule"/>이 돌려주는
/// <see cref="ScheduledTask"/> 핸들에서만 <c>Cancel()</c>을 호출할 수 있습니다. 이 어댑터가
/// <c>ownerId</c>별로 만들어진 <see cref="ScheduledTask"/> 목록을 직접 기억해뒀다가,
/// <see cref="Unschedule"/> 호출 시 그 목록을 찾아 전부 <c>Cancel()</c>합니다.</item>
/// <item><b><see cref="ScheduleCron"/>은 원본에 없는 기능을 조합해서 만듦</b> — 포팅한
/// <see cref="AsyncScheduler"/>는 반복 간격/1회성/매일 정해진 시각만 지원하고 cron 표현식을 직접
/// 해석하지 못합니다. <see cref="IScheduler.ScheduleCron"/> 계약을 만족시키기 위해, 1초 간격으로
/// 반복하는 <see cref="AsyncScheduler.ScheduleRecurring"/>을 하나 등록하고, 매초 <see cref="CronExpression.IsMatch"/>로
/// "지금이 cron 조건에 맞는 순간인지"를 확인해 맞을 때만 실제 콜백을 호출하는 방식으로 구현했습니다 —
/// 초 단위보다 더 정밀한 cron 문법은 다루지 않습니다(<see cref="CronExpression"/> XML 주석 참고).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var scheduler = new AsyncSchedulerAdapter();   // AsyncScheduler.Instance를 감쌈
/// IScheduler ctx = scheduler;
///
/// ctx.SchedulePeriodic("inject-1", TimeSpan.FromSeconds(5), async () => await FireAsync());
/// ctx.ScheduleCron("tag-aggregation-hourly", "0 0 * * * *", async () => await ComputeHourlyAsync());
///
/// // 노드가 종료될 때 — 두 예약 모두 한 번에 취소된다(ownerId가 다르면 서로 영향 없음)
/// ctx.Unschedule("inject-1");
/// </code>
/// </example>
public sealed class AsyncSchedulerAdapter : IScheduler
{
    private readonly AsyncScheduler _inner;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<ScheduledTask>> _byOwner = new();

    /// <summary>앱 전체가 공유하는 <see cref="AsyncScheduler.Instance"/>를 감싸는 어댑터를 만듭니다.</summary>
    public AsyncSchedulerAdapter() : this(AsyncScheduler.Instance) { }

    /// <summary>
    /// 특정 <see cref="AsyncScheduler"/> 인스턴스를 감싸는 어댑터를 만듭니다. 테스트에서 싱글턴 대신
    /// 독립된 인스턴스를 넣어, 여러 테스트가 같은 예약 목록을 공유하지 않게 할 때 사용합니다
    /// (<see cref="EventBusAdapter"/>와 동일한 이유).
    /// </summary>
    public AsyncSchedulerAdapter(AsyncScheduler inner) => _inner = inner;

    /// <inheritdoc/>
    public void SchedulePeriodic(string ownerId, TimeSpan interval, Func<Task> callback)
    {
        var task = _inner.ScheduleRecurring(interval, _ => callback(), ownerId);
        Track(ownerId, task);
    }

    /// <inheritdoc/>
    public void ScheduleCron(string ownerId, string cronExpression, Func<Task> callback)
    {
        var cron = CronExpression.Parse(cronExpression);

        // 위 remarks 참고 — 매초 조건을 확인하고, 맞을 때만 실제 콜백을 호출한다.
        var task = _inner.ScheduleRecurring(TimeSpan.FromSeconds(1), async _ =>
        {
            if (cron.IsMatch(DateTime.Now))
            {
                await callback();
            }
        }, ownerId);

        Track(ownerId, task);
    }

    /// <inheritdoc/>
    public void Unschedule(string ownerId)
    {
        List<ScheduledTask>? tasks;
        lock (_gate)
        {
            if (!_byOwner.Remove(ownerId, out tasks))
            {
                return;
            }
        }

        foreach (var task in tasks)
        {
            task.Cancel();
        }
    }

    private void Track(string ownerId, ScheduledTask task)
    {
        lock (_gate)
        {
            if (!_byOwner.TryGetValue(ownerId, out var list))
            {
                list = new List<ScheduledTask>();
                _byOwner[ownerId] = list;
            }

            list.Add(task);
        }
    }
}
