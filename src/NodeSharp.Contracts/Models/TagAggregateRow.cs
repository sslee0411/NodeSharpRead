namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 태그 집계 행
/// 역활 및 기능 : 태그 원본 이력을 시간별/일별로 요약한 통계 1행을 나타내는 모델
///
/// 태그 원본 이력을 일정 기간(시간별/일별) 단위로 요약한 통계 1행입니다. <see cref="Interfaces.ITagHistorian"/>의
/// <c>RecordAggregateAsync</c>/<c>QueryAggregateAsync</c>가 사용하며, 원본을 매번 다시 스캔하지 않고
/// "일일 생산 리포트" 같은 대시보드를 빠르게 그리기 위한 사전 집계 결과입니다.
/// 설계 근거: 02번 문서 8번 탭 카드 12(v1.20 ITagHistorian 확장).
/// </summary>
/// <param name="PeriodStart">이 집계 구간의 시작 시각(UTC).</param>
/// <param name="PeriodLength">집계 구간의 길이(예: 1시간, 1일).</param>
/// <param name="Avg">구간 내 평균값.</param>
/// <param name="Min">구간 내 최솟값.</param>
/// <param name="Max">구간 내 최댓값.</param>
/// <example>
/// <code>
/// // TagAggregationJob이 매시 정각 원본 이력을 스캔해 시간별 집계 1행을 기록
/// var hourly = new TagAggregateRow(
///     PeriodStart: periodStart, PeriodLength: TimeSpan.FromHours(1),
///     Avg: raw.Average(r => r.Value), Min: raw.Min(r => r.Value), Max: raw.Max(r => r.Value));
/// await historian.RecordAggregateAsync("tag-1", hourly.PeriodStart, hourly.PeriodLength, hourly.Avg, hourly.Min, hourly.Max, ct);
///
/// // 일별 집계는 원본이 아니라 같은 날짜의 시간별 집계 24행을 다시 묶어 계산(성능 최적화)
/// IReadOnlyList&lt;TagAggregateRow&gt; hourlyRows = await historian.QueryAggregateAsync("tag-1", dayStart, dayEnd, TimeSpan.FromHours(1), ct);
/// </code>
/// </example>
public sealed record TagAggregateRow(DateTime PeriodStart, TimeSpan PeriodLength, double Avg, double Min, double Max);
