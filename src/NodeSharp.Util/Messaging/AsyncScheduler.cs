namespace NodeSharp.Util.Messaging;

/// <summary>
/// 주기적/지연/일별 예약 실행을 <c>while(true)</c> 폴링 없이 처리하는 스케줄러입니다.
/// lssLib.Messaging.AsyncScheduler 원본을 구조·이름 그대로 포팅(복사)했습니다 — <c>D:\lssLib</c>를 직접
/// 참조(ProjectReference)하지 않고, 같은 동작을 하는 코드를 NodeSharp.Util로 옮겨왔습니다(포팅 정책,
/// LL-00). 앱 전체에서 하나만 쓰면 되므로 <see cref="Instance"/>로 접근하는 싱글턴입니다(<see cref="EventBus"/>와
/// 동일한 구조).
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서.
/// </summary>
/// <example>
/// <code>
/// // 1) 5초마다 반복
/// var poll = AsyncScheduler.Instance.ScheduleRecurring(
///     TimeSpan.FromSeconds(5), async ct => await ReadSensorAsync(ct), name: "SensorPoll");
///
/// // 2) 3초 뒤 1회만 실행
/// AsyncScheduler.Instance.ScheduleOnce(
///     TimeSpan.FromSeconds(3), async ct => await InitializeAsync(ct), name: "DeviceInit");
///
/// // 3) 매일 오전 2시
/// AsyncScheduler.Instance.ScheduleDailyAt(
///     TimeSpan.FromHours(2), async ct => await CleanupAsync(ct), name: "NightlyCleanup");
///
/// // 4) 제어
/// poll.Pause();
/// poll.Resume();
/// poll.Cancel();
///
/// // 5) 앱 종료 시 전체 정리
/// await AsyncScheduler.Instance.StopAsync();
/// </code>
/// </example>
public sealed class AsyncScheduler
{
    private static readonly Lazy<AsyncScheduler> _instance = new(() => new AsyncScheduler());

    /// <summary>앱 전체에서 공유하는 단일 인스턴스입니다.</summary>
    public static AsyncScheduler Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly List<ScheduledTask> _tasks = new();

    /// <summary>
    /// 새 <see cref="AsyncScheduler"/> 인스턴스를 만듭니다. 앱 코드는 보통 이 생성자 대신
    /// <see cref="Instance"/>를 씁니다. <see cref="EventBus"/>와 같은 이유로, 테스트에서 서로 독립된
    /// 스케줄러가 필요할 때 이 생성자를 씁니다.
    /// </summary>
    public AsyncScheduler() { }

    /// <summary><paramref name="interval"/>마다 <paramref name="callback"/>을 반복 실행하도록 예약합니다.</summary>
    public ScheduledTask ScheduleRecurring(TimeSpan interval, Func<CancellationToken, Task> callback, string name) =>
        Schedule(callback, new ScheduleOptions { Name = name, InitialDelay = interval, Interval = interval });

    /// <summary><paramref name="delay"/>만큼 기다린 뒤 <paramref name="callback"/>을 딱 한 번만 실행하도록 예약합니다.</summary>
    public ScheduledTask ScheduleOnce(TimeSpan delay, Func<CancellationToken, Task> callback, string name) =>
        Schedule(callback, new ScheduleOptions { Name = name, InitialDelay = delay, Interval = TimeSpan.Zero, MaxRuns = 1 });

    /// <summary>
    /// 매일 <paramref name="timeOfDay"/> 시각(자정 기준 경과 시간)에 <paramref name="callback"/>을 실행하도록
    /// 예약합니다. 오늘 그 시각이 이미 지났으면 첫 실행은 내일로 미뤄집니다.
    /// </summary>
    public ScheduledTask ScheduleDailyAt(TimeSpan timeOfDay, Func<CancellationToken, Task> callback, string name)
    {
        var now = DateTime.Now;
        var next = now.Date + timeOfDay;
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return Schedule(callback, new ScheduleOptions { Name = name, InitialDelay = next - now, Interval = TimeSpan.FromDays(1) });
    }

    /// <summary>
    /// <paramref name="options"/>에 담긴 세밀한 설정대로 <paramref name="callback"/>을 예약합니다. 위 3개
    /// 메서드(<see cref="ScheduleRecurring"/>/<see cref="ScheduleOnce"/>/<see cref="ScheduleDailyAt"/>)는 모두
    /// 이 메서드를 자주 쓰는 조합으로 미리 감싸놓은 것입니다.
    /// </summary>
    public ScheduledTask Schedule(Func<CancellationToken, Task> callback, ScheduleOptions options)
    {
        var task = new ScheduledTask(callback, options);
        lock (_gate)
        {
            _tasks.Add(task);
        }

        return task;
    }

    /// <summary>
    /// 지금까지 예약된 모든 작업을 취소하고, 각 작업의 반복 루프가 실제로 끝날 때까지 기다립니다.
    /// 앱(Runner) 종료 시 한 번 호출하면 됩니다.
    /// </summary>
    public async Task StopAsync()
    {
        List<ScheduledTask> snapshot;
        lock (_gate)
        {
            snapshot = _tasks.ToList();
        }

        foreach (var task in snapshot)
        {
            task.Cancel();
        }

        await Task.WhenAll(snapshot.Select(task => task.WaitForCompletionAsync()));
    }
}
