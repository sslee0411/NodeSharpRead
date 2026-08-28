using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="RetentionSweeper"/>(ED-D10, 03번 개발 Step맵)에 대한 단위 테스트입니다. 완료 기준
/// ("각 보관 기간을 초과한 데이터가 RetentionSweeper 실행 후 삭제되는지 확인")을 원본/집계/감사 로그
/// 세 갈래 모두 명시적 시각(<see cref="RetentionSweeper.UtcNowProvider"/>로 고정)과 실제
/// <see cref="SqliteTagHistorian"/>으로 직접 증명합니다. 감사 로그(<c>OP-01</c>, 아직 <c>⏳ 대기</c>)는
/// 저장소 자체가 없으므로 <see cref="RetentionSweeper.PurgeAuditLogAction"/>을 페이크로 주입해
/// "저장소가 있었다면"을 시뮬레이션합니다(클래스 remarks 참고).
/// </summary>
public class RetentionSweeperTests
{
    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-retention-test-{Guid.NewGuid():N}.db");

    /// <summary>등록된 ScheduleCron 호출을 기록만 하는 테스트 전용 <see cref="IScheduler"/>(TagAggregationJobTests.FakeScheduler와 동일한 취지).</summary>
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

    [Fact]
    public async Task RunOnceAsync는_보관_기간을_초과한_원본_데이터를_삭제하고_그_이내는_남긴다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = new DateTime(2026, 8, 28, 3, 0, 0, DateTimeKind.Utc);

            await historian.RecordAsync("tag-1", 1.0, now.AddDays(-40), CancellationToken.None);   // 30일 초과 → 삭제 대상
            await historian.RecordAsync("tag-1", 2.0, now.AddDays(-10), CancellationToken.None);   // 30일 이내 → 유지

            var sweeper = new RetentionSweeper(historian)
            {
                Id = "sweeper-1",
                Policy = new RetentionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(365), TimeSpan.FromDays(365)),
                UtcNowProvider = () => now,
            };

            var result = await sweeper.RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, result.RawDeleted);
            var remaining = await historian.QueryAsync("tag-1", now.AddDays(-100), now, CancellationToken.None);
            var row = Assert.Single(remaining);
            Assert.Equal(2.0, row.Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunOnceAsync는_보관_기간을_초과한_집계_데이터를_삭제하고_그_이내는_남긴다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = new DateTime(2026, 8, 28, 3, 0, 0, DateTimeKind.Utc);

            await historian.RecordAggregateAsync("tag-1", now.AddDays(-400), TimeSpan.FromHours(1), 1, 1, 1, CancellationToken.None);   // 1년 초과 → 삭제 대상
            await historian.RecordAggregateAsync("tag-1", now.AddDays(-100), TimeSpan.FromHours(1), 2, 2, 2, CancellationToken.None);   // 1년 이내 → 유지

            var sweeper = new RetentionSweeper(historian)
            {
                Id = "sweeper-1",
                Policy = new RetentionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(365), TimeSpan.FromDays(365)),
                UtcNowProvider = () => now,
            };

            var result = await sweeper.RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, result.AggregateDeleted);
            var remaining = await historian.QueryAggregateAsync("tag-1", now.AddYears(-2), now, TimeSpan.FromHours(1), CancellationToken.None);
            var row = Assert.Single(remaining);
            Assert.Equal(2.0, row.Avg);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task PurgeAuditLogAction이_주입되면_AuditLogRetention_컷오프로_호출되고_결과에_반영된다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = new DateTime(2026, 8, 28, 1, 0, 0, DateTimeKind.Utc);
            var policy = new RetentionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(365), TimeSpan.FromDays(365));

            DateTime? receivedCutoff = null;
            var sweeper = new RetentionSweeper(historian)
            {
                Id = "sweeper-1",
                Policy = policy,
                UtcNowProvider = () => now,
                PurgeAuditLogAction = (cutoff, ct) =>
                {
                    receivedCutoff = cutoff;
                    return Task.FromResult(7);   // "감사 로그 저장소가 있었다면 7건 삭제됨" 시뮬레이션
                },
            };

            var result = await sweeper.RunOnceAsync(CancellationToken.None);

            Assert.Equal(7, result.AuditLogDeleted);
            Assert.Equal(now - policy.AuditLogRetention, receivedCutoff);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task PurgeAuditLogAction을_지정하지_않으면_감사_로그_삭제_건수는_0이다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var sweeper = new RetentionSweeper(historian) { Id = "sweeper-1" };   // PurgeAuditLogAction 미지정

            var result = await sweeper.RunOnceAsync(CancellationToken.None);

            Assert.Equal(0, result.AuditLogDeleted);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task StartAsync는_매일_새벽_1시_Cron을_Id로_등록한다()
    {
        var fake = new FakeScheduler();
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var sweeper = new RetentionSweeper(historian) { Id = "sweeper-42", Scheduler = fake };

            await sweeper.StartAsync(CancellationToken.None);

            var registration = Assert.Single(fake.CronRegistrations);
            Assert.Equal("sweeper-42", registration.OwnerId);
            Assert.Equal("0 0 1 * * *", registration.CronExpression);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task StopAsync는_Scheduler_Unschedule을_Id로_호출한다()
    {
        var fake = new FakeScheduler();
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var sweeper = new RetentionSweeper(historian) { Id = "sweeper-7", Scheduler = fake };
            await sweeper.StartAsync(CancellationToken.None);

            await sweeper.StopAsync();

            Assert.Equal("sweeper-7", fake.UnscheduledOwnerId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void historian이_null이면_ArgumentNullException을_던진다()
    {
        Assert.Throws<ArgumentNullException>(() => new RetentionSweeper(null!));
    }

    [Fact]
    public void Policy를_지정하지_않으면_기본값_30일_1년_1년_을_사용한다()
    {
        Assert.Equal(TimeSpan.FromDays(30), RetentionPolicy.Default.RawDataRetention);
        Assert.Equal(TimeSpan.FromDays(365), RetentionPolicy.Default.AggregatedRetention);
        Assert.Equal(TimeSpan.FromDays(365), RetentionPolicy.Default.AuditLogRetention);
    }
}
