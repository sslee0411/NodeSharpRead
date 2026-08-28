using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="HistorianIntegrityChecker"/>(ED-D09, 03번 개발 Step맵)에 대한 단위 테스트입니다. 완료
/// 기준("DB를 인위적으로 손상시킨 뒤 기동하면 최신 백업으로 자동 복원되고, 백업조차 없으면 빈 DB로
/// 재초기화되며 경고가 남는지 확인")의 세 갈래(정상/백업으로 복원/빈 DB로 재초기화)를 실제 SQLite
/// 파일과 <see cref="IEventBus"/> 발행 여부로 직접 증명합니다. 백업 원본 자체(<c>OP-09</c>, 아직
/// <c>⏳ 대기</c>)는 없으므로 <see cref="HistorianIntegrityChecker.RestoreFromLatestBackupAction"/>을
/// 페이크로 주입해 "백업이 있었다면"을 시뮬레이션합니다(클래스 remarks 참고).
/// </summary>
public class HistorianIntegrityCheckerTests
{
    /// <summary>발행된 이벤트를 그대로 기록만 하는 테스트 전용 <see cref="IEventBus"/>.</summary>
    private sealed class FakeEventBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) => throw new NotSupportedException();

        public void Publish<TEvent>(TEvent evt) => Published.Add(evt!);
    }

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-integrity-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task 손상되지_않은_DB는_Ok를_반환하고_아무_이벤트도_발행하지_않는다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);   // 정상 스키마로 생성
            await historian.RecordAsync("tag-1", 1.0, DateTime.UtcNow, CancellationToken.None);

            var bus = new FakeEventBus();
            var checker = new HistorianIntegrityChecker { DbPath = dbPath, EventBus = bus };

            var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

            Assert.Equal(HistorianIntegrityOutcome.Ok, outcome);
            Assert.Empty(bus.Published);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task DB_파일이_아직_없으면_최초_기동으로_보고_Ok를_반환한다()
    {
        var dbPath = NewTempDbPath();   // 생성한 적 없음 — 파일 자체가 없음
        var bus = new FakeEventBus();
        var checker = new HistorianIntegrityChecker { DbPath = dbPath, EventBus = bus };

        var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

        Assert.Equal(HistorianIntegrityOutcome.Ok, outcome);
        Assert.Empty(bus.Published);
        Assert.False(File.Exists(dbPath));   // 없던 파일을 새로 만들지도 않음(최초 기동은 이 Step 범위 밖)
    }

    [Fact]
    public async Task 손상된_DB에_백업_복원이_성공하면_RestoredFromBackup을_반환하고_이벤트를_발행한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            await File.WriteAllBytesAsync(dbPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });   // 인위적 손상(SQLite 형식 아님)

            var bus = new FakeEventBus();
            var restoreCalled = false;
            var checker = new HistorianIntegrityChecker
            {
                DbPath = dbPath,
                EventBus = bus,
                RestoreFromLatestBackupAction = (path, ct) =>
                {
                    restoreCalled = true;
                    Assert.Equal(dbPath, path);
                    return Task.FromResult(true);   // "백업이 있었다면" 시뮬레이션
                },
            };

            var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

            Assert.Equal(HistorianIntegrityOutcome.RestoredFromBackup, outcome);
            Assert.True(restoreCalled);
            var evt = Assert.Single(bus.Published);
            var integrityEvent = Assert.IsType<HistorianIntegrityEvent>(evt);
            Assert.Equal(dbPath, integrityEvent.DbPath);
            Assert.True(integrityEvent.RestoredFromBackup);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task 손상된_DB에_사용가능한_백업이_없으면_빈_DB로_재초기화하고_경고_이벤트를_발행한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            await File.WriteAllBytesAsync(dbPath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });   // 인위적 손상

            var bus = new FakeEventBus();
            var checker = new HistorianIntegrityChecker
            {
                DbPath = dbPath,
                EventBus = bus,
                // RestoreFromLatestBackupAction 미지정 — "백업 없음"(OP-09 미착수 시점의 기본 동작)
            };

            var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

            Assert.Equal(HistorianIntegrityOutcome.ReinitializedEmpty, outcome);
            var evt = Assert.Single(bus.Published);
            var integrityEvent = Assert.IsType<HistorianIntegrityEvent>(evt);
            Assert.False(integrityEvent.RestoredFromBackup);
            Assert.False(string.IsNullOrWhiteSpace(integrityEvent.Message));   // 완료 기준의 "경고가 남는지"

            // 재초기화된 DB가 실제로 빈 상태의 정상 스키마인지(SqliteTagHistorian으로 바로 사용 가능한지) 확인
            var reopened = new SqliteTagHistorian(dbPath);
            var rows = await reopened.QueryAsync("tag-1", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), CancellationToken.None);
            Assert.Empty(rows);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task 백업_복원_시도가_false를_반환하면_재초기화_경로로_넘어간다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            await File.WriteAllBytesAsync(dbPath, new byte[] { 0x12, 0x34 });

            var bus = new FakeEventBus();
            var checker = new HistorianIntegrityChecker
            {
                DbPath = dbPath,
                EventBus = bus,
                RestoreFromLatestBackupAction = (_, _) => Task.FromResult(false),   // 복원 시도했지만 백업 없음
            };

            var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

            Assert.Equal(HistorianIntegrityOutcome.ReinitializedEmpty, outcome);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task IntegrityCheckAction을_주입하면_실제_SQLite_파일_대신_그_결과를_따른다()
    {
        var dbPath = NewTempDbPath();
        var checker = new HistorianIntegrityChecker
        {
            DbPath = dbPath,
            IntegrityCheckAction = (_, _) => Task.FromResult(false),   // 실제로는 정상 파일이지만 강제로 "손상"
            RestoreFromLatestBackupAction = (_, _) => Task.FromResult(true),
        };

        var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

        Assert.Equal(HistorianIntegrityOutcome.RestoredFromBackup, outcome);
    }

    [Fact]
    public async Task EventBus를_지정하지_않아도_기본_어댑터로_예외_없이_동작한다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            await File.WriteAllBytesAsync(dbPath, new byte[] { 0x00 });
            var checker = new HistorianIntegrityChecker { DbPath = dbPath };   // EventBus 미지정

            var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);

            Assert.Equal(HistorianIntegrityOutcome.ReinitializedEmpty, outcome);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
