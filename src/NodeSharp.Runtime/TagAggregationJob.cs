using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Util.Messaging;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 태그 집계 이력 계산 배치
/// 역활 및 기능 : 매시 정각 시간별 평균/최소/최대 집계 행을 계산해 기록하고, 매일 자정 어제 하루치
/// 일별 집계를 시간별 집계 24행으로부터(원본 재조회 없이) 계산해 기록하는 공유 서비스
///
/// (ED-D08c, v1.14 신설 — 재검토로 발견) ED-D10 Retention의 <c>AggregatedRetention</c>(집계 데이터
/// 1년 보관)이 전제하는 "시간/일별 평균·최대·최소" 계산 배치가 어디에도 없던 공백을 메웁니다 —
/// <c>RetentionSweeper</c>(ED-D10)는 원본만 삭제할 뿐 집계 행을 만들지 않으므로, 이 클래스가 먼저
/// 집계 행을 채워야 ED-D10이 지울 "집계 데이터"가 실제로 존재하게 됩니다.
/// 설계 근거: 03번 Step맵 ED-D08c, <see cref="ITagHistorian"/> XML 주석("TagAggregationJob이
/// IScheduler로 매시/매일 배치를 등록해 RecordAggregateAsync를 채운다").
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><see cref="TagIds"/>·<see cref="Scheduler"/> 주입 방식은 <see cref="DeviceMapPoller"/>와
/// 동일한 관례</b> — 태그 목록은 배포마다 고정된 순수 데이터라 <c>IStructureService</c>를 거치지 않고
/// 그대로 주입받고, <see cref="Scheduler"/>가 <c>null</c>이면 <see cref="StartAsync"/>가 기본
/// <see cref="AsyncSchedulerAdapter"/>를 직접 생성합니다.</item>
/// <item><b>완료 기준의 "변화 없는 태그는 생략"은 "그 구간에 원본 기록이 없는 태그는 생략"으로
/// 구현</b> — <see cref="ITagHistorian"/> 클래스 문서가 예고한 SDT(Swinging Door Trending) 압축이
/// 도입되면 값이 변하지 않는 태그는 애초에 <c>RecordAsync</c> 자체가 새 행을 남기지 않게 될 것이므로,
/// 이 배치 입장에서는 "값이 변하지 않았다"와 "그 구간에 원본 행이 없다"가 같은 신호입니다. 지금은
/// SDT 압축이 아직 구현되지 않았지만(별도 Step, 이 Step 범위 밖), <see cref="AggregateHourAsync"/>가
/// "구간에 원본 행이 0개면 집계 행을 만들지 않고 false 반환"으로 동작해 SDT 도입 이전/이후 모두
/// 같은 규칙으로 완료 기준을 만족합니다.</item>
/// <item><b>일별 집계는 시간별 평균의 단순 평균</b> — <see cref="TagAggregateRow"/>가 시간대별 원본
/// 표본 개수를 담지 않으므로(설계 범위 밖), 표본 수 가중 평균이 아니라 24개(또는 그보다 적은) 시간별
/// <c>Avg</c>의 산술 평균을 사용합니다. 표본 수 가중이 필요해지면 <c>TagAggregateRow</c>에 개수
/// 필드를 추가하는 후속 Step에서 다룰 사안입니다.</item>
/// <item><b><see cref="UtcNowProvider"/>로 시각을 주입 가능하게 함</b> — <see cref="StartAsync"/>가
/// 등록하는 두 Cron 콜백은 "방금 끝난 시간/어제 하루"를 계산하기 위해 현재 시각이 필요한데, 이번
/// 세션에서 겪은 AsyncScheduler 타이밍 테스트 불안정(실제 시간 경과를 기다리는 테스트의 취약성,
/// RT-08 Step맵 기록 참고)과 같은 함정을 피하기 위해 실제 대기 없이 결정적 시각을 주입해 콜백
/// 로직 자체를 테스트할 수 있도록 했습니다. 기본값은 실제 <c>DateTime.UtcNow</c>이므로 운영 동작은
/// 바뀌지 않습니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var job = new TagAggregationJob(historian)
/// {
///     Id = "tag-aggregation",
///     TagIds = new[] { "tag-1", "tag-2" },
/// };
/// await job.StartAsync(CancellationToken.None);   // 매시 정각 + 매일 자정 Cron 등록
///
/// // 완료 기준 직접 검증(스케줄러를 거치지 않고 명시적 구간으로 즉시 호출)
/// bool wrote = await job.AggregateHourAsync("tag-1", hourStart, ct);          // 원본이 있으면 true
/// bool wroteDaily = await job.AggregateDayAsync("tag-1", dayStart, ct);       // 시간별 집계 재사용, 원본 재조회 없음
/// </code>
/// </example>
public sealed class TagAggregationJob : ISharedServiceNode
{
    private readonly ITagHistorian _historian;
    private IScheduler? _activeScheduler;

    /// <inheritdoc />
    /// <remarks>이 집계 배치 자체의 식별자입니다 — 같은 배치를 가리키는 인스턴스는 항상 같은 Id를 가져야 합니다(<see cref="ISharedServiceNode.Id"/> 문서 참고).</remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>(ED-D08c) 매시/매일 집계를 계산할 태그 Id 목록 — 클래스 remarks의 "TagIds" 항목 참고.</summary>
    public IReadOnlyList<string> TagIds { get; init; } = Array.Empty<string>();

    /// <summary>(<see cref="DeviceMapPoller.Scheduler"/>와 동일한 관례) 지정하지 않으면 <see cref="StartAsync"/>가 기본 <see cref="AsyncSchedulerAdapter"/>를 직접 생성합니다.</summary>
    public IScheduler? Scheduler { get; set; }

    /// <summary>(클래스 remarks의 "UtcNowProvider" 항목 참고) 기본값은 실제 <c>DateTime.UtcNow</c>이며, 테스트에서만 결정적 시각으로 교체합니다.</summary>
    public Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    /// <summary>이력을 읽고/쓸 <see cref="ITagHistorian"/>을 받습니다.</summary>
    public TagAggregationJob(ITagHistorian historian) =>
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));

    /// <summary>
    /// <see cref="Scheduler"/>(없으면 기본 <see cref="AsyncSchedulerAdapter"/>)에 <see cref="Id"/>를
    /// ownerId 삼아 매시 정각(<c>"0 0 * * * *"</c>) 시간별 집계, 매일 자정(<c>"0 0 0 * * *"</c>)
    /// 일별 집계를 각각 등록합니다. 두 등록 모두 같은 <see cref="Id"/>를 ownerId로 쓰므로
    /// <see cref="StopAsync"/> 한 번으로 둘 다 취소됩니다.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        _activeScheduler = Scheduler ?? new AsyncSchedulerAdapter();

        _activeScheduler.ScheduleCron(Id, "0 0 * * * *", async () =>
        {
            var now = UtcNowProvider();
            var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-1);
            foreach (var tagId in TagIds)
            {
                await AggregateHourAsync(tagId, hourStart, ct).ConfigureAwait(false);
            }
        });

        _activeScheduler.ScheduleCron(Id, "0 0 0 * * *", async () =>
        {
            var now = UtcNowProvider();
            var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1);
            foreach (var tagId in TagIds)
            {
                await AggregateDayAsync(tagId, dayStart, ct).ConfigureAwait(false);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// <paramref name="tagId"/>의 <paramref name="hourStart"/>~<c>+1시간</c> 구간 원본 값을 조회해
    /// 평균/최소/최대 집계 행 1개를 기록합니다. 그 구간에 원본 행이 하나도 없으면(=값이 변하지
    /// 않아 SDT 압축으로 기록되지 않았거나, 애초에 폴링이 없었던 경우) 아무것도 기록하지 않고
    /// <c>false</c>를 반환합니다(클래스 remarks의 "변화 없는 태그는 생략" 항목 참고).
    /// </summary>
    /// <returns>집계 행을 기록했으면 <c>true</c>, 원본이 없어 생략했으면 <c>false</c>.</returns>
    public async Task<bool> AggregateHourAsync(string tagId, DateTime hourStart, CancellationToken ct)
    {
        var raw = await _historian.QueryAsync(tagId, hourStart, hourStart.AddHours(1), ct).ConfigureAwait(false);
        if (raw.Count == 0)
        {
            return false;
        }

        var avg = raw.Average(r => r.Value);
        var min = raw.Min(r => r.Value);
        var max = raw.Max(r => r.Value);
        await _historian.RecordAggregateAsync(tagId, hourStart, TimeSpan.FromHours(1), avg, min, max, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// <paramref name="tagId"/>의 <paramref name="dayStart"/> 하루치 일별 집계 행 1개를, 원본을 다시
    /// 스캔하지 않고 같은 날짜의 시간별(<see cref="TimeSpan.FromHours(double)"/>(1)) 집계 행들만
    /// <see cref="ITagHistorian.QueryAggregateAsync"/>로 조회해 계산·기록합니다(클래스 remarks의
    /// "일별 집계는 시간별 평균의 단순 평균" 항목 참고). 그 날짜에 시간별 집계 행이 하나도 없으면
    /// 아무것도 기록하지 않고 <c>false</c>를 반환합니다.
    /// </summary>
    /// <returns>집계 행을 기록했으면 <c>true</c>, 시간별 집계가 없어 생략했으면 <c>false</c>.</returns>
    public async Task<bool> AggregateDayAsync(string tagId, DateTime dayStart, CancellationToken ct)
    {
        var hourlyRows = await _historian
            .QueryAggregateAsync(tagId, dayStart, dayStart.AddDays(1), TimeSpan.FromHours(1), ct)
            .ConfigureAwait(false);
        if (hourlyRows.Count == 0)
        {
            return false;
        }

        var avg = hourlyRows.Average(r => r.Avg);
        var min = hourlyRows.Min(r => r.Min);
        var max = hourlyRows.Max(r => r.Max);
        await _historian.RecordAggregateAsync(tagId, dayStart, TimeSpan.FromDays(1), avg, min, max, ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        _activeScheduler?.Unschedule(Id);
        _activeScheduler = null;
        return Task.CompletedTask;
    }
}
