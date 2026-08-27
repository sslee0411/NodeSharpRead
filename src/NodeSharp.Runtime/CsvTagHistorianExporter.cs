using System.Globalization;
using System.Text;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : CSV 태그 이력 내보내기
/// 역활 및 기능 : <see cref="ITagHistorianExporter"/>(CT-04c)의 1차 구현체 — <see cref="ITagHistorian"/>에
/// 저장된 태그 이력을 CSV 파일로 내보냄
///
/// 02번 설계 문서 8번 탭 카드12 "★ 발견한 공백(연동)"이 예고한 CSV 내보내기 계약의 1차 구현체입니다.
/// <see cref="SqliteTagHistorian"/>(ED-D08a)와 같은 레이어(NodeSharp.Runtime)에 배치해, "인터페이스는
/// Contracts, 1차 구현체는 Runtime" 원칙을 그대로 따릅니다.
/// 설계 근거: 02번 문서 8번 탭 카드 12.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>여러 태그 내보내기는 "넓은" 형식(wide format)</b> — <see cref="ITagHistorianExporter"/> XML
/// 주석이 "한 파일에 컬럼별로 묶어 내보낸다"고 명시했으므로, 태그마다 별도 열(column)을 만들고 행은
/// 시각(<c>Timestamp</c>) 하나로 통일합니다. 서로 다른 태그가 서로 다른 시각에 기록됐을 수 있으므로,
/// 내보내는 모든 태그의 기록 시각을 합집합(중복 제거)해 오름차순으로 정렬한 뒤, 각 행에서 그 시각에
/// 값이 없는 태그의 칸은 빈 칸으로 남겨둡니다(표준 시계열 wide-format CSV의 통상적인 처리).</item>
/// <item><b>단일 태그 오버로드는 배열 오버로드로 위임</b> — <c>SecurityHistorianInterfacesTests.cs</c>의
/// <c>FakeTagHistorianExporter</c> 스텁(CT-04c)이 이미 이 위임 관계로 테스트됐던 것과 동일한 관례를
/// 실제 구현체에도 그대로 적용(코드 중복 없이 태그 1개짜리 배열로 그대로 재사용).</item>
/// <item><b>숫자·시각 서식은 항상 불변 문화권(InvariantCulture)</b> — 시스템 로캘의 소수점 구분자
/// 차이(일부 유럽어 로캘은 쉼표를 소수점으로 씀)에 영향받지 않도록, 값은 <c>double.ToString
/// (CultureInfo.InvariantCulture)</c>, 시각은 라운드트립 가능한 <c>"o"</c> 서식의
/// <c>DateTime.ToString("o", CultureInfo.InvariantCulture)</c>으로 고정합니다.</item>
/// <item><b>CSV 필드 이스케이프는 이 Step 범위 밖</b> — 태그 Id는 식별자 성격의 문자열이라 쉼표/따옴표를
/// 포함하지 않는다고 전제합니다(실제로 콤마를 포함하는 TagId를 만들 수 없도록 막는 것은 태그 이름
/// 검증 로직의 책임이며 이 Step의 범위가 아님).</item>
/// <item><b>같은 태그가 정확히 같은 시각(Ticks 단위)에 두 번 기록되는 경우는 범위 밖</b> — 실제 폴링
/// 주기(<see cref="DeviceMapPoller"/> 등)로는 발생하지 않는다고 전제합니다(발생하면 나중에 기록된
/// 값으로 덮어써 내보냄).</item>
/// <item><b>완료 기준의 "MesReportNode가 동일 인터페이스로 컴파일되는지"는 이 Step 범위 밖</b> —
/// <c>MesReportNode</c>(NR-08, Phase 13)가 아직 존재하지 않음을 확인(03번 Step맵 NR-08 여전히
/// ⏳ 대기) — ED-D07b가 알람 목록 UI 부재로 부분 보류됐던 것과 동일한 유형의 공백. 이 Step은
/// <c>ExportCsvAsync</c> 자체(완료 기준의 앞부분, "CSV가 ED-D08a 저장 값과 일치")만 지금 완전히
/// 구현·검증하고, <c>MesReportNode</c> 컴파일 확인은 NR-08 착수 시점으로 미룸 — 인터페이스
/// 시그니처를 바꾸지 않는 한 이 클래스 자체를 다시 손댈 필요는 없을 것으로 예상되지만, 그 확인
/// 자체가 이뤄지기 전까지는 이 Step도 최종 "✅ 완료"로 전환하지 않음(확립된 관례).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// ITagHistorian historian = new SqliteTagHistorian(@"C:\NodeSharpRead\history.db");
/// ITagHistorianExporter exporter = new CsvTagHistorianExporter(historian);
///
/// // 1) 태그 1개
/// await exporter.ExportCsvAsync("tag-1", from, to, @"C:\Reports\tag-1.csv", ct);
///
/// // 2) 여러 태그를 한 파일에 컬럼별로("Timestamp,tag-1,tag-2,tag-3" 헤더)
/// await exporter.ExportCsvAsync(new[] { "tag-1", "tag-2", "tag-3" }, from, to, @"C:\Reports\daily.csv", ct);
/// </code>
/// </example>
public sealed class CsvTagHistorianExporter : ITagHistorianExporter
{
    private readonly ITagHistorian _historian;

    /// <summary>내보낼 원본 이력을 조회할 <see cref="ITagHistorian"/>을 받습니다.</summary>
    public CsvTagHistorianExporter(ITagHistorian historian) =>
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));

    /// <inheritdoc/>
    public Task ExportCsvAsync(string tagId, DateTime from, DateTime to, string outputPath, CancellationToken ct) =>
        ExportCsvAsync(new[] { tagId }, from, to, outputPath, ct);

    /// <inheritdoc/>
    public async Task ExportCsvAsync(IReadOnlyList<string> tagIds, DateTime from, DateTime to, string outputPath, CancellationToken ct)
    {
        if (tagIds is null || tagIds.Count == 0)
            throw new ArgumentException("tagIds는 최소 1개 이상이어야 합니다.", nameof(tagIds));

        // 태그별 원본 이력 조회 + "시각 → 값" 조회 사전 구성(같은 태그 내 시각 중복은 나중 값으로 덮어씀)
        var lookups = new Dictionary<string, Dictionary<DateTime, double>>();
        var allTimestamps = new SortedSet<DateTime>();

        foreach (var tagId in tagIds)
        {
            var rows = await _historian.QueryAsync(tagId, from, to, ct);
            var lookup = new Dictionary<DateTime, double>();
            foreach (var (at, value) in rows)
            {
                lookup[at] = value;
                allTimestamps.Add(at);
            }
            lookups[tagId] = lookup;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("Timestamp");
        foreach (var tagId in tagIds)
        {
            sb.Append(',').Append(tagId);
        }
        sb.Append('\n');

        foreach (var at in allTimestamps)
        {
            sb.Append(at.ToString("o", CultureInfo.InvariantCulture));
            foreach (var tagId in tagIds)
            {
                sb.Append(',');
                if (lookups[tagId].TryGetValue(at, out var value))
                {
                    sb.Append(value.ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append('\n');
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }
}
