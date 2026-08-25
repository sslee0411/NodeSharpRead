using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SqliteTagHistorian"/>(ED-D08a, 02번 설계 문서 8번 탭 카드12)에 대한 단위 테스트입니다.
/// 완료 기준("태그 값 변경이 기록되고 재기동 후에도 이력 조회가 가능한지 확인")을 직접 증명하기
/// 위해, "재기동"은 같은 DB 파일 경로를 가리키는 <b>서로 다른 <see cref="SqliteTagHistorian"/>
/// 인스턴스</b>로 시뮬레이션합니다(새 인스턴스 = 새 프로세스가 다시 그 파일을 여는 것과 동일한
/// 코드 경로 — 생성자가 매번 <c>CREATE TABLE IF NOT EXISTS</c>로만 스키마를 보장하고 기존 데이터는
/// 건드리지 않으므로).
/// </summary>
public class SqliteTagHistorianTests
{
    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-historian-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RecordAsync로_기록한_값을_QueryAsync_구간_안에서_조회한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 8.7, now, CancellationToken.None);

            var result = await historian.QueryAsync("tag-1", now.AddHours(-1), now.AddHours(1), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(8.7, result[0].Value);
            Assert.Equal(now, result[0].At);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync는_구간_밖에_기록된_값은_제외한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 8.7, now, CancellationToken.None);
            await historian.RecordAsync("tag-1", 9.1, now.AddDays(-2), CancellationToken.None); // 구간 밖

            var result = await historian.QueryAsync("tag-1", now.AddHours(-1), now.AddHours(1), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(8.7, result[0].Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync는_서로_다른_TagId를_섞지_않는다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var now = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 1.0, now, CancellationToken.None);
            await historian.RecordAsync("tag-2", 2.0, now, CancellationToken.None);

            var result = await historian.QueryAsync("tag-1", now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(1.0, result[0].Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync는_같은_태그의_여러_값을_시간순으로_반환한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var t0 = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 3.0, t0.AddSeconds(2), CancellationToken.None);
            await historian.RecordAsync("tag-1", 1.0, t0, CancellationToken.None);
            await historian.RecordAsync("tag-1", 2.0, t0.AddSeconds(1), CancellationToken.None);

            var result = await historian.QueryAsync("tag-1", t0.AddSeconds(-1), t0.AddSeconds(3), CancellationToken.None);

            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, result.Select(r => r.Value));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RecordAggregateAsync로_기록한_집계행을_QueryAggregateAsync로_조회한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var periodStart = DateTime.UtcNow;

            await historian.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromHours(1), avg: 8.5, min: 8.0, max: 9.1, CancellationToken.None);

            var rows = await historian.QueryAggregateAsync("tag-1", periodStart.AddMinutes(-1), periodStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);

            Assert.Single(rows);
            Assert.Equal(8.5, rows[0].Avg);
            Assert.Equal(8.0, rows[0].Min);
            Assert.Equal(9.1, rows[0].Max);
            Assert.Equal(TimeSpan.FromHours(1), rows[0].PeriodLength);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task QueryAggregateAsync는_PeriodLength가_다른_집계행은_제외한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var periodStart = DateTime.UtcNow;

            await historian.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromHours(1), avg: 8.5, min: 8.0, max: 9.1, CancellationToken.None);
            await historian.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromDays(1), avg: 8.4, min: 7.0, max: 9.5, CancellationToken.None);

            var hourlyRows = await historian.QueryAggregateAsync("tag-1", periodStart.AddMinutes(-1), periodStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);

            Assert.Single(hourlyRows);
            Assert.Equal(8.5, hourlyRows[0].Avg);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task 재기동_시뮬레이션__같은_파일_경로로_새_인스턴스를_열어도_원본_값을_그대로_조회한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var firstProcess = new SqliteTagHistorian(dbPath);
            var now = DateTime.UtcNow;
            await firstProcess.RecordAsync("tag-1", 42.0, now, CancellationToken.None);

            // "재기동" = 같은 DB 파일을 가리키는 완전히 새로운 인스턴스 (새 프로세스가 다시 여는 것과 동일 경로)
            var afterRestart = new SqliteTagHistorian(dbPath);
            var result = await afterRestart.QueryAsync("tag-1", now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(42.0, result[0].Value);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task 재기동_시뮬레이션__집계행도_새_인스턴스에서_그대로_조회된다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var firstProcess = new SqliteTagHistorian(dbPath);
            var periodStart = DateTime.UtcNow;
            await firstProcess.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromHours(1), avg: 5.0, min: 4.0, max: 6.0, CancellationToken.None);

            var afterRestart = new SqliteTagHistorian(dbPath);
            var rows = await afterRestart.QueryAggregateAsync("tag-1", periodStart.AddMinutes(-1), periodStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);

            Assert.Single(rows);
            Assert.Equal(5.0, rows[0].Avg);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void 존재하지_않는_파일_경로로_생성하면_새_DB_파일이_만들어진다()
    {
        var dbPath = NewTempDbPath();
        Assert.False(File.Exists(dbPath));
        try
        {
            _ = new SqliteTagHistorian(dbPath);

            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void 중첩된_디렉터리_경로를_주면_디렉터리를_자동_생성한다()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nodesharp-historian-dir-{Guid.NewGuid():N}", "nested");
        var dbPath = Path.Combine(dir, "history.db");
        Assert.False(Directory.Exists(dir));
        try
        {
            _ = new SqliteTagHistorian(dbPath);

            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void dbPath가_비어있으면_ArgumentException을_던진다(string dbPath)
    {
        Assert.Throws<ArgumentException>(() => new SqliteTagHistorian(dbPath));
    }
}
