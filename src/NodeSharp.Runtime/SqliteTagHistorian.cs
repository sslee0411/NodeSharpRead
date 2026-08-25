using Microsoft.Data.Sqlite;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : SQLite 기반 태그 이력 저장소
/// 역활 및 기능 : <see cref="ITagHistorian"/>(CT-04c)의 1차 구현체 — 태그 원본 값과 사전 집계 행을 SQLite 파일에 기록·조회
///
/// 02번 설계 문서 8번 탭 카드12가 예고한 "1차 구현(SqliteTagHistorian, NodeSharp.Runtime)은
/// lssLib.DB.Sqlite를 포팅해 사용" 방침을 따르되, 이 참조 소스인 lssLib 저장소에는
/// 이식 가능한 <c>DB.Sqlite</c> 모듈이 실제로 존재하지 않음을 grep으로 확인(2026-08-25) — Node-RED의
/// localfilesystem/sqlite Context Storage 참고 스킴과 동일한 최소 스키마를 NuGet 공식
/// <c>Microsoft.Data.Sqlite</c> 패키지로 직접 구현했습니다(FileContextStore가 lssLib
/// JsonWriteService 부재 시 Newtonsoft.Json으로 직접 구현한 것과 동일한 처리 원칙, RT-09c 참고).
/// 설계 근거: 02번 문서 8번 탭 카드 12.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>연결 수명</b>: 인스턴스 생성 시 연결을 계속 열어두지 않고, 호출마다 짧은 수명의
/// <see cref="SqliteConnection"/>을 열고 닫습니다 — 이 클래스가 관리하는 데이터가 저빈도(폴링 주기
/// 단위)라 연결 풀링 오버헤드가 문제되지 않고, "재기동 후에도 조회 가능"이라는 완료 기준을 같은
/// DB 파일을 가리키는 서로 다른 인스턴스 2개로 직접 증명하기에도 이 방식이 가장 단순합니다.</item>
/// <item><b>연결 문자열에 Pooling=False(사용자 Windows 실행에서 발견·수정, 2026-08-25)</b>: Microsoft.
/// Data.Sqlite는 기본적으로 연결 풀링이 켜져 있어, <see cref="SqliteConnection"/>을 Dispose해도(Close)
/// 실제 네이티브 sqlite3 파일 핸들은 풀에 반환될 뿐 즉시 해제되지 않습니다 — Windows는 파일이 열려
/// 있으면 삭제를 거부하기 때문에(Linux와 달리), xUnit 테스트가 임시 DB 파일을 정리하려고
/// <c>File.Delete</c>를 호출하면 <c>IOException</c>("사용 중인 파일")이 발생했습니다(사용자가 실제
/// dotnet test 실행에서 재현·보고, SqliteTagHistorianTests 13건 중 파일/디렉터리를 실제로 만들고
/// 정리하는 10건 전부가 같은 근본 원인으로 실패). 연결 문자열에 <c>Pooling=False</c>를 추가해 Close 시
/// 풀에 반환하지 않고 즉시 실제로 닫도록 수정 — 이 클래스는 애초에 호출마다 새 연결을 여는 방식이라
/// 풀링이 주는 성능 이점 자체가 크지 않아, 안전하게(파일 잠금 없이) 즉시 닫히는 쪽을 우선했습니다.</item>
/// <item><b>시각 저장 방식</b>: <see cref="DateTime"/>을 문자열로 직렬화하는 대신 <c>Ticks</c>(long)로
/// 저장합니다 — SQL의 부등호 비교(<c>&gt;=</c>/<c>&lt;=</c>)로 구간 필터링을 그대로 수행할 수 있고,
/// 문자열 포맷/타임존 파싱 오차가 없습니다. 저장/조회 모두 UTC를 전제로 합니다(설계 문서 예제가
/// 전부 <c>DateTime.UtcNow</c> 사용).</item>
/// <item><b>SDT(Swinging Door Trending) 압축은 이 Step 범위 밖</b> — <see cref="ITagHistorian"/> XML
/// 주석이 "값이 거의 변하지 않는 태그는 SDT 압축으로 저장량을 줄일 수 있다"고 명시했지만 이는
/// 선택적 최적화("~할 수 있다")이지 완료 기준(태그 값 변경 기록 + 재기동 후 조회)의 필수 조건이
/// 아닙니다 — 모든 <see cref="RecordAsync"/> 호출을 압축 없이 그대로 저장하는 1차 구현으로 완료
/// 기준을 먼저 증명하고, SDT는 저장량 문제가 실제로 불거지는 시점(ED-D10 Retention 등)에 별도
/// 검토합니다.</item>
/// <item><b>스키마 생성</b>: 생성자에서 <c>CREATE TABLE IF NOT EXISTS</c>로 스키마를 보장합니다 —
/// 파일이 없으면 SQLite가 새로 만들고, 이미 있으면(재기동 시나리오) 기존 데이터를 그대로 유지한
/// 채 스키마만 재확인합니다. 대상 경로의 디렉터리가 없으면 생성합니다(예: %AppData%\NodeSharpRead\
/// history.db처럼 중첩 경로를 바로 넘겨도 되도록).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 폴링 때마다 원본 값 기록 (DeviceMapPoller/PlcTagReadNode 등이 향후 연동 예정, 이 Step 범위 밖)
/// ITagHistorian historian = new SqliteTagHistorian(@"C:\NodeSharpRead\history.db");
/// await historian.RecordAsync("tag-1", 8.7, DateTime.UtcNow, ct);
///
/// // 2) 재기동(새 프로세스) 이후에도 같은 파일 경로로 새 인스턴스를 열면 이전 값을 그대로 조회 가능
/// ITagHistorian reopened = new SqliteTagHistorian(@"C:\NodeSharpRead\history.db");
/// var rows = await reopened.QueryAsync("tag-1", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, ct);
/// </code>
/// </example>
public sealed class SqliteTagHistorian : ITagHistorian
{
    private readonly string _connectionString;

    /// <summary>
    /// SQLite 파일 경로를 받아 스키마를 보장합니다. 파일/상위 디렉터리가 없으면 새로 생성됩니다.
    /// </summary>
    /// <param name="dbPath">SQLite DB 파일 경로(절대/상대 모두 허용).</param>
    public SqliteTagHistorian(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath는 비어 있을 수 없습니다.", nameof(dbPath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Pooling=False: Close() 시 풀에 반환하지 않고 실제로 즉시 닫아 파일 잠금을 남기지 않음
        // (사용자 Windows 실행에서 발견된 IOException 수정, 클래스 remarks "연결 문자열에 Pooling=False" 참고)
        _connectionString = $"Data Source={dbPath};Pooling=False";
        EnsureSchema();
    }

    /// <summary>원본/집계 테이블·인덱스를 없으면 생성합니다(있으면 기존 데이터 유지, 재기동 시나리오).</summary>
    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TagHistory (
                TagId TEXT NOT NULL,
                AtTicks INTEGER NOT NULL,
                Value REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TagHistory_TagId_AtTicks ON TagHistory (TagId, AtTicks);

            CREATE TABLE IF NOT EXISTS TagAggregate (
                TagId TEXT NOT NULL,
                PeriodStartTicks INTEGER NOT NULL,
                PeriodLengthTicks INTEGER NOT NULL,
                Avg REAL NOT NULL,
                Min REAL NOT NULL,
                Max REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TagAggregate_TagId_PeriodStartTicks ON TagAggregate (TagId, PeriodStartTicks);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public async Task RecordAsync(string tagId, double value, DateTime at, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO TagHistory (TagId, AtTicks, Value) VALUES ($tagId, $at, $value);";
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.Parameters.AddWithValue("$at", at.Ticks);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(DateTime At, double Value)>> QueryAsync(string tagId, DateTime from, DateTime to, CancellationToken ct)
    {
        var results = new List<(DateTime At, double Value)>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT AtTicks, Value FROM TagHistory
            WHERE TagId = $tagId AND AtTicks >= $from AND AtTicks <= $to
            ORDER BY AtTicks ASC;
            """;
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.Parameters.AddWithValue("$from", from.Ticks);
        cmd.Parameters.AddWithValue("$to", to.Ticks);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((new DateTime(reader.GetInt64(0), DateTimeKind.Utc), reader.GetDouble(1)));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task RecordAggregateAsync(string tagId, DateTime periodStart, TimeSpan periodLength, double avg, double min, double max, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TagAggregate (TagId, PeriodStartTicks, PeriodLengthTicks, Avg, Min, Max)
            VALUES ($tagId, $periodStart, $periodLength, $avg, $min, $max);
            """;
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.Parameters.AddWithValue("$periodStart", periodStart.Ticks);
        cmd.Parameters.AddWithValue("$periodLength", periodLength.Ticks);
        cmd.Parameters.AddWithValue("$avg", avg);
        cmd.Parameters.AddWithValue("$min", min);
        cmd.Parameters.AddWithValue("$max", max);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TagAggregateRow>> QueryAggregateAsync(string tagId, DateTime from, DateTime to, TimeSpan periodLength, CancellationToken ct)
    {
        var results = new List<TagAggregateRow>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT PeriodStartTicks, PeriodLengthTicks, Avg, Min, Max FROM TagAggregate
            WHERE TagId = $tagId AND PeriodLengthTicks = $periodLength
                  AND PeriodStartTicks >= $from AND PeriodStartTicks <= $to
            ORDER BY PeriodStartTicks ASC;
            """;
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.Parameters.AddWithValue("$periodLength", periodLength.Ticks);
        cmd.Parameters.AddWithValue("$from", from.Ticks);
        cmd.Parameters.AddWithValue("$to", to.Ticks);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new TagAggregateRow(
                PeriodStart: new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
                PeriodLength: new TimeSpan(reader.GetInt64(1)),
                Avg: reader.GetDouble(2),
                Min: reader.GetDouble(3),
                Max: reader.GetDouble(4)));
        }

        return results;
    }
}
