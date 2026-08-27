using System.Globalization;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="CsvTagHistorianExporter"/>(ED-D08b, 02번 설계 문서 8번 탭 카드12 "★ 발견한 공백(연동)")에
/// 대한 단위 테스트입니다. 완료 기준의 앞부분("ExportCsvAsync 결과 CSV가 ED-D08a 저장 값과 일치하는지")을
/// 실제 <see cref="SqliteTagHistorian"/>(ED-D08a)에 값을 기록한 뒤 그대로 내보내는 왕복으로 직접
/// 증명합니다 — 완료 기준의 뒷부분("MesReportNode가 동일 인터페이스로 컴파일되는지")은 MesReportNode
/// 자체가 아직 없어(NR-08, Phase 13, ⏳ 대기) 이 테스트 범위 밖입니다(클래스 remarks 참고).
/// </summary>
public class CsvTagHistorianExporterTests
{
    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-exporter-test-{Guid.NewGuid():N}.db");

    private static string NewTempCsvPath() =>
        Path.Combine(Path.GetTempPath(), $"nodesharp-exporter-test-{Guid.NewGuid():N}.csv");

    [Fact]
    public async Task 단일_태그_내보내기는_SqliteTagHistorian에_기록된_값과_시각을_그대로_담는다()
    {
        var dbPath = NewTempDbPath();
        var csvPath = NewTempCsvPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 8.7, t0, CancellationToken.None);
            await historian.RecordAsync("tag-1", 9.1, t0.AddSeconds(1), CancellationToken.None);

            await exporter.ExportCsvAsync("tag-1", t0.AddSeconds(-1), t0.AddSeconds(2), csvPath, CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(csvPath);
            Assert.Equal("Timestamp,tag-1", lines[0]);
            Assert.Equal(3, lines.Length);   // 헤더 1 + 데이터 2
            Assert.Equal($"{t0.ToString("o", CultureInfo.InvariantCulture)},8.7", lines[1]);
            Assert.Equal($"{t0.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture)},9.1", lines[2]);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task 단일_태그_오버로드는_배열_오버로드와_동일한_CSV를_만든다()
    {
        var dbPath = NewTempDbPath();
        var csvSingle = NewTempCsvPath();
        var csvArray = NewTempCsvPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;
            await historian.RecordAsync("tag-1", 1.23, t0, CancellationToken.None);

            await exporter.ExportCsvAsync("tag-1", t0.AddSeconds(-1), t0.AddSeconds(1), csvSingle, CancellationToken.None);
            await exporter.ExportCsvAsync(new[] { "tag-1" }, t0.AddSeconds(-1), t0.AddSeconds(1), csvArray, CancellationToken.None);

            Assert.Equal(await File.ReadAllTextAsync(csvArray), await File.ReadAllTextAsync(csvSingle));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(csvSingle)) File.Delete(csvSingle);
            if (File.Exists(csvArray)) File.Delete(csvArray);
        }
    }

    [Fact]
    public async Task 여러_태그_내보내기는_컬럼별로_묶고_값이_없는_시각은_빈칸으로_남긴다()
    {
        var dbPath = NewTempDbPath();
        var csvPath = NewTempCsvPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;

            // tag-1은 t0에만, tag-2는 t0+1초에만 기록 — 서로 다른 시각
            await historian.RecordAsync("tag-1", 10.0, t0, CancellationToken.None);
            await historian.RecordAsync("tag-2", 20.0, t0.AddSeconds(1), CancellationToken.None);

            await exporter.ExportCsvAsync(new[] { "tag-1", "tag-2" }, t0.AddSeconds(-1), t0.AddSeconds(2), csvPath, CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(csvPath);
            Assert.Equal("Timestamp,tag-1,tag-2", lines[0]);
            Assert.Equal(3, lines.Length);   // 헤더 1 + 시각 합집합 2개(t0, t0+1초)
            Assert.Equal($"{t0.ToString("o", CultureInfo.InvariantCulture)},10,", lines[1]);          // tag-2 칸은 빈 칸
            Assert.Equal($"{t0.AddSeconds(1).ToString("o", CultureInfo.InvariantCulture)},,20", lines[2]);   // tag-1 칸은 빈 칸
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task 여러_태그가_같은_시각에_값이_있으면_한_행에_함께_담긴다()
    {
        var dbPath = NewTempDbPath();
        var csvPath = NewTempCsvPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;

            await historian.RecordAsync("tag-1", 1.0, t0, CancellationToken.None);
            await historian.RecordAsync("tag-2", 2.0, t0, CancellationToken.None);

            await exporter.ExportCsvAsync(new[] { "tag-1", "tag-2" }, t0.AddSeconds(-1), t0.AddSeconds(1), csvPath, CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(csvPath);
            Assert.Equal(2, lines.Length);   // 헤더 1 + 데이터 1행(같은 시각이라 한 행에 합쳐짐)
            Assert.Equal($"{t0.ToString("o", CultureInfo.InvariantCulture)},1,2", lines[1]);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task 구간에_기록된_값이_없으면_헤더만_있는_CSV를_만든다()
    {
        var dbPath = NewTempDbPath();
        var csvPath = NewTempCsvPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;

            await exporter.ExportCsvAsync("tag-1", t0.AddDays(-1), t0, csvPath, CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(csvPath);
            Assert.Single(lines);
            Assert.Equal("Timestamp,tag-1", lines[0]);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task 중첩된_디렉터리_경로를_주면_디렉터리를_자동_생성한다()
    {
        var dbPath = NewTempDbPath();
        var dir = Path.Combine(Path.GetTempPath(), $"nodesharp-exporter-dir-{Guid.NewGuid():N}", "nested");
        var csvPath = Path.Combine(dir, "out.csv");
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);
            var t0 = DateTime.UtcNow;
            await historian.RecordAsync("tag-1", 1.0, t0, CancellationToken.None);

            Assert.False(Directory.Exists(dir));

            await exporter.ExportCsvAsync("tag-1", t0.AddSeconds(-1), t0.AddSeconds(1), csvPath, CancellationToken.None);

            Assert.True(File.Exists(csvPath));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(dir)) Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public async Task tagIds가_비어있으면_ArgumentException을_던진다()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var historian = new SqliteTagHistorian(dbPath);
            var exporter = new CsvTagHistorianExporter(historian);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                exporter.ExportCsvAsync(Array.Empty<string>(), DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, "unused.csv", CancellationToken.None));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void historian이_null이면_ArgumentNullException을_던진다()
    {
        Assert.Throws<ArgumentNullException>(() => new CsvTagHistorianExporter(null!));
    }
}
