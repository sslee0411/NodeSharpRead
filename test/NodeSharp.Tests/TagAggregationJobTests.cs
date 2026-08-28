using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TagAggregationJob"/>(ED-D08c, 03번 Step맵 — 태그 집계 이력 계산 배치)에 대한 단위
/// 테스트입니다. 완료 기준 두 가지 — "값이 여러 번 바뀐 태그는 정각마다 집계 행이 1개씩 생성되고
/// 변화 없는 태그는 생략되는지"(<see cref="AggregateHourAsync는_원본_값이_있으면_평균_최소_최대_집계행을_기록하고_true를_반환한다"/>/
/// <see cref="AggregateHourAsync는_그_시간에_원본_값이_없으면_집계행을_생략하고_false를_반환한다"/>)와
/// "일별 집계가 시간별 집계 24개로부터 원본 재조회 없이 계산되는지"(<see cref="AggregateDayAsync는_시간별_집계로부터_계산하고_원본_QueryAsync를_한번도_호출하지_않는다"/>) —
/// 를 실 시간 경과를 기다리지 않고 명시적 구간(hourStart/dayStart)을 직접 넘겨 결정적으로 증명합니다
/// (DeviceMapPoller.PollOnceAsync·이번 세션에서 겪은 AsyncScheduler 타이밍 테스트 교훈과 동일한
/// 취지 — 클래스 remarks 참고).
/// </summary>
public class TagAggregationJobTests
{
    /// <summary>QueryAsync/QueryAggregateAsync 호출 횟수를 각각 세는 테스트 전용 <see cref="ITagHistorian"/> — "원본 재조회 없음"을 직접 증명하기 위함.</summary>
    private sealed class CallCountingHistorian : ITagHistorian
    {
        private readonly List<(string TagId, DateTime At, double Value)> _raw = new();
        private readonly List<(string TagId, TagAggregateRow Row)> _aggregates = new();

        public int QueryAsyncCallCount { get; private set; }

        public int QueryAggregateAsyncCallCount { get; private set; }

        public Task RecordAsync(string tagId, double value, DateTime at, CancellationToken ct)
        {
            _raw.Add((tagId, at, value));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(DateTime At, double Value)>> QueryAsync(string tagId, DateTime from, DateTime to, CancellationToken ct)
        {
            QueryAsyncCallCount++;
            IReadOnlyList<(DateTime At, double Value)> result = _raw
                .Where(r => r.TagId == tagId && r.At >= from && r.At < to)
                .Select(r => (r.At, r.Value))
                .ToList();
            return Task.FromResult(result);
        }

        public Task RecordAggregateAsync(string tagId, DateTime periodStart, TimeSpan periodLength, double avg, double min, double max, CancellationToken ct)
        {
            _aggregates.Add((tagId, new TagAggregateRow(periodStart, periodLength, avg, min, max)));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagAggregateRow>> QueryAggregateAsync(string tagId, DateTime from, DateTime to, TimeSpan periodLength, CancellationToken ct)
        {
            QueryAggregateAsyncCallCount++;
            IReadOnlyList<TagAggregateRow> result = _aggregates
                .Where(a => a.TagId == tagId && a.Row.PeriodLength == periodLength && a.Row.PeriodStart >= from && a.Row.PeriodStart < to)
                .Select(a => a.Row)
                .ToList();
            return Task.FromResult(result);
        }

        /// <summary>(ED-D10) 이 테스트 파일의 완료 기준과 무관해 최소 구현만 제공 — 실제 삭제 동작 검증은 RetentionSweeperTests가 담당.</summary>
        public Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct)
        {
            var removed = _raw.RemoveAll(r => r.At < cutoff);
            return Task.FromResult(removed);
        }

        /// <summary>(ED-D10) 위와 동일한 이유로 최소 구현만 제공.</summary>
        public Task<int> PurgeAggregateOlderThanAsync(DateTime cutoff, CancellationToken ct)
        {
            var removed = _aggregates.RemoveAll(a => a.Row.PeriodStart < cutoff);
            return Task.FromResult(removed);
        }
    }

    /// <summary>등록된 ScheduleCron 호출(ownerId/cron식/콜백)을 모두 기록만 하는 테스트 전용 <see cref="IScheduler"/>(DeviceMapPollerTests.FakeScheduler와 동일한 취지).</summary>
    private sealed class FakeScheduler : IScheduler
    {
        public List<(string OwnerId, string CronExpression, Func<Task> Callback)> CronRegistrations { get; } = new();

        public string? UnscheduledOwnerId { get; private set; }

        public void SchedulePeriodic(string ownerId, TimeSpan interval, Func<Task> callback)
        {
        }

        public void ScheduleCron(string ownerId, string cronExpression, Func<Task> callback) =>
            CronRegistrations.Add((ownerId, cronExpression, callback));

        public void Unschedule(string ownerId) => UnscheduledOwnerId = ownerId;
    }

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-aggregation-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AggregateHourAsync는_원본_값이_있으면_평균_최소_최대_집계행을_기록하고_true를_반환한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var job = new TagAggregationJob(historian) { Id = "agg-1", TagIds = new[] { "tag-1" } };
            var hourStart = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);

            // 값이 여러 번 바뀐 태그(완료 기준의 "값이 여러 번 바뀐 태그" 절반)
            await historian.RecordAsync("tag-1", 10.0, hourStart.AddMinutes(5), CancellationToken.None);
            await historian.RecordAsync("tag-1", 20.0, hourStart.AddMinutes(15), CancellationToken.None);
            await historian.RecordAsync("tag-1", 15.0, hourStart.AddMinutes(45), CancellationToken.None);

            var wrote = await job.AggregateHourAsync("tag-1", hourStart, CancellationToken.None);

            Assert.True(wrote);
            var rows = await historian.QueryAggregateAsync("tag-1", hourStart, hourStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);
            var row = Assert.Single(rows);
            Assert.Equal(hourStart, row.PeriodStart);
            Assert.Equal(TimeSpan.FromHours(1), row.PeriodLength);
            Assert.Equal(15.0, row.Avg);
            Assert.Equal(10.0, row.Min);
            Assert.Equal(20.0, row.Max);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task AggregateHourAsync는_그_시간에_원본_값이_없으면_집계행을_생략하고_false를_반환한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var job = new TagAggregationJob(historian) { Id = "agg-1", TagIds = new[] { "tag-1" } };
            var hourStart = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);

            // 변화 없는 태그(완료 기준의 "변화 없는 태그는 생략" 절반) — 이 구간에 원본 기록 자체가 없음
            var wrote = await job.AggregateHourAsync("tag-1", hourStart, CancellationToken.None);

            Assert.False(wrote);
            var rows = await historian.QueryAggregateAsync("tag-1", hourStart, hourStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);
            Assert.Empty(rows);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task AggregateDayAsync는_시간별_집계로부터_계산하고_원본_QueryAsync를_한번도_호출하지_않는다()
    {
        var historian = new CallCountingHistorian();
        var job = new TagAggregationJob(historian) { Id = "agg-1", TagIds = new[] { "tag-1" } };
        var dayStart = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);

        // 완료 기준의 "일별 집계가 시간별 집계 24개로부터" — 24시간 전부 서로 다른 값으로 시간별 집계를 미리 채움
        // (AggregateHourAsync를 거치지 않고 RecordAggregateAsync를 직접 호출해, 이 테스트가 순수하게
        // AggregateDayAsync만을 검증하도록 함)
        for (var h = 0; h < 24; h++)
        {
            await historian.RecordAggregateAsync("tag-1", dayStart.AddHours(h), TimeSpan.FromHours(1), avg: h, min: h - 0.5, max: h + 0.5, CancellationToken.None);
        }

        var wrote = await job.AggregateDayAsync("tag-1", dayStart, CancellationToken.None);

        Assert.True(wrote);
        // 완료 기준의 "원본 재조회 없이" — QueryAsync(원본 조회)는 단 한 번도 호출되지 않아야 한다
        Assert.Equal(0, historian.QueryAsyncCallCount);
        Assert.True(historian.QueryAggregateAsyncCallCount >= 1);

        var dailyRows = await historian.QueryAggregateAsync("tag-1", dayStart, dayStart.AddDays(1), TimeSpan.FromDays(1), CancellationToken.None);
        var daily = Assert.Single(dailyRows);
        Assert.Equal(dayStart, daily.PeriodStart);
        Assert.Equal(TimeSpan.FromDays(1), daily.PeriodLength);
        Assert.Equal(11.5, daily.Avg);   // 0..23의 평균
        Assert.Equal(-0.5, daily.Min);   // h=0일 때 min(h-0.5)
        Assert.Equal(23.5, daily.Max);   // h=23일 때 max(h+0.5)
    }

    [Fact]
    public async Task AggregateDayAsync는_그_날짜에_시간별_집계가_없으면_생략하고_false를_반환하며_원본을_조회하지_않는다()
    {
        var historian = new CallCountingHistorian();
        var job = new TagAggregationJob(historian) { Id = "agg-1", TagIds = new[] { "tag-1" } };
        var dayStart = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);

        var wrote = await job.AggregateDayAsync("tag-1", dayStart, CancellationToken.None);

        Assert.False(wrote);
        Assert.Equal(0, historian.QueryAsyncCallCount);
    }

    [Fact]
    public async Task StartAsync는_시간별_집계와_일별_집계_두_Cron을_모두_같은_Id로_등록한다()
    {
        var fake = new FakeScheduler();
        var historian = new CallCountingHistorian();
        var job = new TagAggregationJob(historian) { Id = "agg-42", TagIds = Array.Empty<string>(), Scheduler = fake };

        await job.StartAsync(CancellationToken.None);

        Assert.Equal(2, fake.CronRegistrations.Count);
        Assert.All(fake.CronRegistrations, r => Assert.Equal("agg-42", r.OwnerId));
        Assert.Contains(fake.CronRegistrations, r => r.CronExpression == "0 0 * * * *");    // 매시 정각
        Assert.Contains(fake.CronRegistrations, r => r.CronExpression == "0 0 0 * * *");    // 매일 자정
    }

    [Fact]
    public async Task StartAsync가_등록한_시간별_콜백을_호출하면_UtcNowProvider_기준_직전_시간을_집계한다()
    {
        var fake = new FakeScheduler();
        var historian = new SqliteTagHistorian(NewTempDbPath());
        var fixedNow = new DateTime(2026, 8, 27, 10, 3, 0, DateTimeKind.Utc);   // 10시 3분 → 직전 시간은 9시~10시
        var expectedHourStart = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);

        var job = new TagAggregationJob(historian)
        {
            Id = "agg-1",
            TagIds = new[] { "tag-1" },
            Scheduler = fake,
            UtcNowProvider = () => fixedNow,
        };
        await historian.RecordAsync("tag-1", 100.0, expectedHourStart.AddMinutes(10), CancellationToken.None);

        await job.StartAsync(CancellationToken.None);
        var hourlyCallback = fake.CronRegistrations.Single(r => r.CronExpression == "0 0 * * * *").Callback;
        await hourlyCallback();

        var rows = await historian.QueryAggregateAsync("tag-1", expectedHourStart, expectedHourStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(100.0, row.Avg);
    }

    [Fact]
    public async Task StopAsync는_Scheduler_Unschedule을_Id로_호출한다()
    {
        var fake = new FakeScheduler();
        var historian = new CallCountingHistorian();
        var job = new TagAggregationJob(historian) { Id = "agg-7", TagIds = Array.Empty<string>(), Scheduler = fake };
        await job.StartAsync(CancellationToken.None);

        await job.StopAsync();

        Assert.Equal("agg-7", fake.UnscheduledOwnerId);
    }

    [Fact]
    public void historian이_null이면_ArgumentNullException을_던진다()
    {
        Assert.Throws<ArgumentNullException>(() => new TagAggregationJob(null!));
    }
}
