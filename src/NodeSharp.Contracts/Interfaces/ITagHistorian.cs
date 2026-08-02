using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

// 한글명: 태그 이력 저장소 계약
/// <summary>
/// 태그 값의 시계열 이력을 기록·조회하는 계약입니다. 원본(<c>RecordAsync</c>/<c>QueryAsync</c>)과
/// 사전 집계(<c>RecordAggregateAsync</c>/<c>QueryAggregateAsync</c>) 두 층을 함께 선언합니다(v1.20 확장판).
/// 설계 근거: 02번 문서 8번 탭 카드 12.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>1차 구현(<c>SqliteTagHistorian</c>, NodeSharp.Runtime)은 <c>lssLib.DB.Sqlite</c>를 포팅해 사용하며,
/// 값이 거의 변하지 않는 태그는 SDT(Swinging Door Trending) 압축으로 저장량을 줄일 수 있습니다.</item>
/// <item><c>TagAggregationJob</c>(<see cref="ISharedServiceNode"/>)이 <see cref="IScheduler"/>로 매시/매일
/// 배치를 등록해 <c>RecordAggregateAsync</c>를 채웁니다 — 일별 집계는 원본을 다시 스캔하지 않고 같은
/// 날짜의 시간별 집계를 재활용합니다.</item>
/// <item>오래된 원본/집계 데이터를 언제까지 보관할지(retention)는 아직 이 인터페이스의 책임 범위가
/// 아닙니다 — 02번 문서 8번 탭에 "발견한 공백"으로 남아 있으며 별도 Step에서 다룰 예정입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) PlcTagReadNode가 폴링 때마다 원본 값을 기록
/// await historian.RecordAsync("tag-1", value: 8.7, at: DateTime.UtcNow, ct);
///
/// // 2) Editor [구조 설정] 탭의 "최근 이력" 미니 차트 미리보기
/// IReadOnlyList&lt;(DateTime At, double Value)&gt; recent = await historian.QueryAsync("tag-1", from, to, ct);
///
/// // 3) TagAggregationJob이 매시 정각 시간별 집계를 기록하고, 일일 리포트가 이를 조회
/// await historian.RecordAggregateAsync("tag-1", periodStart, TimeSpan.FromHours(1), avg: 8.5, min: 8.0, max: 9.1, ct);
/// IReadOnlyList&lt;TagAggregateRow&gt; hourlyRows = await historian.QueryAggregateAsync("tag-1", dayStart, dayEnd, TimeSpan.FromHours(1), ct);
/// </code>
/// </example>
public interface ITagHistorian
{
    /// <summary>태그의 원본 값 1건을 시각과 함께 기록합니다.</summary>
    Task RecordAsync(string tagId, double value, DateTime at, CancellationToken ct);

    /// <summary>지정한 구간의 원본 값을 시각순으로 조회합니다.</summary>
    Task<IReadOnlyList<(DateTime At, double Value)>> QueryAsync(string tagId, DateTime from, DateTime to, CancellationToken ct);

    /// <summary>지정한 구간의 평균/최솟값/최댓값을 사전 집계 행으로 기록합니다(<see cref="TagAggregateRow"/>).</summary>
    Task RecordAggregateAsync(string tagId, DateTime periodStart, TimeSpan periodLength, double avg, double min, double max, CancellationToken ct);

    /// <summary>지정한 구간의 사전 집계 행을 조회합니다. 원본을 다시 스캔하지 않아 대시보드 조회가 빠릅니다.</summary>
    Task<IReadOnlyList<TagAggregateRow>> QueryAggregateAsync(string tagId, DateTime from, DateTime to, TimeSpan periodLength, CancellationToken ct);
}
