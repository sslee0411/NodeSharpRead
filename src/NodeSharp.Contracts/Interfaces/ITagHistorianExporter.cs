namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 태그 이력 내보내기 계약
/// 역활 및 기능 : 저장된 태그 이력을 CSV로 내보내는 계약
///
/// <see cref="ITagHistorian"/>에 조회만 되고 내보낼 방법이 없던 공백을 메우는 계약입니다 — 저장된
/// 태그 이력을 CSV 파일로 내보내 Excel/보고서 도구 등 외부 시스템에서 활용할 수 있게 합니다.
/// 설계 근거: 02번 문서 8번 탭 카드 12("★ 발견한 공백(연동)").
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Editor [구조 설정] 탭의 "이력" 미리보기 패널에 "CSV로 내보내기" 버튼으로 단일 태그 내보내기를
/// 연결합니다(파일 저장 다이얼로그로 즉시 저장).</item>
/// <item>9번 탭 <c>MesReportNode</c>가 이 인터페이스를 내부적으로 재사용합니다 — "사람이 수동으로
/// 내보내는 것"과 "Flow가 자동으로 전송하는 것"이 같은 코드 경로를 탑니다(정기 리포트 자동화).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 태그 1개를 CSV로 내보내기(Editor 수동 내보내기 버튼)
/// await exporter.ExportCsvAsync("tag-1", from, to, outputPath: "C:\\Reports\\tag-1.csv", ct);
///
/// // 2) 여러 태그를 한 파일에 컬럼별로 묶어 내보내기 — 설비별 "일일 생산 리포트"
/// await exporter.ExportCsvAsync(new[] { "tag-1", "tag-2", "tag-3" }, from, to, outputPath: "C:\\Reports\\daily.csv", ct);
///
/// // 3) MesReportNode(Flow)가 같은 Exporter를 자동으로 호출 — 수동 내보내기와 코드 재사용
/// public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
///     exporter.ExportCsvAsync(tagIds, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, mesOutputPath, ct);
/// </code>
/// </example>
public interface ITagHistorianExporter
{
    /// <summary>태그 1개의 지정한 구간 이력을 CSV 파일로 내보냅니다.</summary>
    Task ExportCsvAsync(string tagId, DateTime from, DateTime to, string outputPath, CancellationToken ct);

    /// <summary>여러 태그의 지정한 구간 이력을 한 CSV 파일에 컬럼별로 묶어 내보냅니다.</summary>
    Task ExportCsvAsync(IReadOnlyList<string> tagIds, DateTime from, DateTime to, string outputPath, CancellationToken ct);
}
