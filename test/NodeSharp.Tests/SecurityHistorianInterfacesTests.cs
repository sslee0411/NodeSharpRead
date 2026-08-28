using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ICredentialStore"/>/<see cref="ITagHistorian"/>/<see cref="ITagHistorianExporter"/>/
/// <see cref="TagAggregateRow"/>(CT-04c, 02번 설계 문서 9번 탭 "Credential 암호화 저장", 8번 탭 카드12)에
/// 대한 단위 테스트입니다. 인터페이스 자체는 동작이 없으므로, 여기서는 최소 스텁 구현이 실제로
/// 컴파일·동작하는지를 확인합니다.
/// </summary>
public class SecurityHistorianInterfacesTests
{
    /// <summary>테스트 전용 <see cref="ICredentialStore"/> 스텁 — 메모리 Dictionary로 Set/Get, Save/Load 호출 여부 기록.</summary>
    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<(string, string), string> _values = new();
        public string? LastSavedPath { get; private set; }
        public string? LastLoadedPath { get; private set; }

        public void Set(string nodeId, string field, string plainValue) => _values[(nodeId, field)] = plainValue;
        public string? Get(string nodeId, string field) => _values.TryGetValue((nodeId, field), out var v) ? v : null;
        public void Save(string path) => LastSavedPath = path;
        public void Load(string path) => LastLoadedPath = path;
    }

    /// <summary>테스트 전용 <see cref="ITagHistorian"/> 스텁 — 원본/집계 데이터를 메모리 리스트에 보관.</summary>
    private sealed class FakeTagHistorian : ITagHistorian
    {
        private readonly List<(string TagId, DateTime At, double Value)> _raw = new();
        private readonly List<(string TagId, TagAggregateRow Row)> _aggregates = new();

        public Task RecordAsync(string tagId, double value, DateTime at, CancellationToken ct)
        {
            _raw.Add((tagId, at, value));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(DateTime At, double Value)>> QueryAsync(string tagId, DateTime from, DateTime to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(DateTime At, double Value)>>(
                _raw.Where(r => r.TagId == tagId && r.At >= from && r.At <= to).Select(r => (r.At, r.Value)).ToList());

        public Task RecordAggregateAsync(string tagId, DateTime periodStart, TimeSpan periodLength, double avg, double min, double max, CancellationToken ct)
        {
            _aggregates.Add((tagId, new TagAggregateRow(periodStart, periodLength, avg, min, max)));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagAggregateRow>> QueryAggregateAsync(string tagId, DateTime from, DateTime to, TimeSpan periodLength, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TagAggregateRow>>(
                _aggregates.Where(a => a.TagId == tagId && a.Row.PeriodStart >= from && a.Row.PeriodStart <= to).Select(a => a.Row).ToList());

        /// <summary>(ED-D10) 이 스텁은 이 테스트 파일의 완료 기준과 무관해 최소 구현만 제공 — 실제 삭제 동작 검증은 SqliteTagHistorianTests/RetentionSweeperTests가 담당.</summary>
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

    /// <summary>테스트 전용 <see cref="ITagHistorianExporter"/> 스텁 — 마지막 내보내기 호출 인자만 기록.</summary>
    private sealed class FakeTagHistorianExporter : ITagHistorianExporter
    {
        public IReadOnlyList<string>? LastTagIds { get; private set; }
        public string? LastOutputPath { get; private set; }

        public Task ExportCsvAsync(string tagId, DateTime from, DateTime to, string outputPath, CancellationToken ct) =>
            ExportCsvAsync(new[] { tagId }, from, to, outputPath, ct);

        public Task ExportCsvAsync(IReadOnlyList<string> tagIds, DateTime from, DateTime to, string outputPath, CancellationToken ct)
        {
            LastTagIds = tagIds;
            LastOutputPath = outputPath;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ICredentialStore_Set으로_저장한_값을_Get으로_그대로_조회한다()
    {
        var store = new FakeCredentialStore();

        store.Set("mqtt-1", "password", "s3cr3t");

        Assert.Equal("s3cr3t", store.Get("mqtt-1", "password"));
        Assert.Null(store.Get("mqtt-1", "unknown-field"));
    }

    [Fact]
    public void ICredentialStore_Save와_Load는_전달한_경로를_그대로_기록한다()
    {
        var store = new FakeCredentialStore();

        store.Save("credentials.json");
        store.Load("credentials.json");

        Assert.Equal("credentials.json", store.LastSavedPath);
        Assert.Equal("credentials.json", store.LastLoadedPath);
    }

    [Fact]
    public async Task ITagHistorian_RecordAsync로_기록한_값을_QueryAsync_구간_안에서만_조회한다()
    {
        var historian = new FakeTagHistorian();
        var now = DateTime.UtcNow;

        await historian.RecordAsync("tag-1", 8.7, now, CancellationToken.None);
        await historian.RecordAsync("tag-1", 9.1, now.AddDays(-2), CancellationToken.None); // 구간 밖

        var result = await historian.QueryAsync("tag-1", now.AddHours(-1), now.AddHours(1), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(8.7, result[0].Value);
    }

    [Fact]
    public async Task ITagHistorian_RecordAggregateAsync로_기록한_집계행을_QueryAggregateAsync로_조회한다()
    {
        var historian = new FakeTagHistorian();
        var periodStart = DateTime.UtcNow;

        await historian.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromHours(1), avg: 8.5, min: 8.0, max: 9.1, CancellationToken.None);

        var rows = await historian.QueryAggregateAsync("tag-1", periodStart.AddMinutes(-1), periodStart.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal(8.5, rows[0].Avg);
        Assert.Equal(8.0, rows[0].Min);
        Assert.Equal(9.1, rows[0].Max);
    }

    [Fact]
    public async Task ITagHistorianExporter_단일태그_오버로드는_배열_오버로드로_위임된다()
    {
        var exporter = new FakeTagHistorianExporter();
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow;

        await exporter.ExportCsvAsync("tag-1", from, to, "out.csv", CancellationToken.None);

        Assert.Equal(new[] { "tag-1" }, exporter.LastTagIds);
        Assert.Equal("out.csv", exporter.LastOutputPath);
    }
}
