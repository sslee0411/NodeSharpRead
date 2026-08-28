namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 보관 기간 정책
/// 역활 및 기능 : Historian 원본/집계, 감사 로그의 보관 기간(며칠 지나면 지울지)을 담는 순수 데이터
///
/// (ED-D10) 02번 설계 문서 8번 탭 카드14가 예고한 "태그 이력·감사 로그 보존 기간 정책"의 순수 데이터
/// 레코드입니다 — 카드12 <see cref="Interfaces.ITagHistorian"/>과 7번 탭 <c>AuditEntry</c>(append-only
/// 감사 로그)는 "어떻게 저장하는지"만 정의했을 뿐 "언제까지 보관하는지"가 없던 공백을 메웁니다.
/// 설계 근거: 02번 문서 8번 탭 카드 14.
/// </summary>
/// <param name="RawDataRetention">원본(1초/1분 단위) Historian 데이터 보관 기간 — 기본 30일.</param>
/// <param name="AggregatedRetention">시간별/일별 집계 Historian 데이터 보관 기간 — 기본 1년(원본보다 저장 용량이 훨씬 작음).</param>
/// <param name="AuditLogRetention">감사 로그(Deploy/TagWrite/AlarmAck 등) 보관 기간 — 기본 1년(산업 규정 준수 목적상 원본보다 길게).</param>
/// <remarks>
/// <see cref="AuditLogRetention"/>은 <c>RetentionSweeper</c>(ED-D10)가 실제로 소비하지만, 감사 로그
/// 저장소(<c>AuditEntry</c>/<c>OP-01</c>, 아직 <c>⏳ 대기</c>) 자체가 없어 그 정리 동작은 지금 델리게이트
/// 자리표시자로만 연결돼 있습니다(<c>RetentionSweeper.PurgeAuditLogAction</c> 클래스 remarks 참고) —
/// 이 필드 자체는 <c>OP-01</c> 완성 전에도 정책값으로는 의미가 있어 미리 선언해둡니다.
/// </remarks>
/// <example>
/// <code>
/// var policy = new RetentionPolicy(
///     RawDataRetention: TimeSpan.FromDays(30),
///     AggregatedRetention: TimeSpan.FromDays(365),
///     AuditLogRetention: TimeSpan.FromDays(365));
/// </code>
/// </example>
public sealed record RetentionPolicy(TimeSpan RawDataRetention, TimeSpan AggregatedRetention, TimeSpan AuditLogRetention)
{
    /// <summary>02번 설계 문서 8번 탭 카드14 표의 기본값(원본 30일/집계 1년/감사로그 1년)으로 만든 정책.</summary>
    public static RetentionPolicy Default { get; } = new(
        RawDataRetention: TimeSpan.FromDays(30),
        AggregatedRetention: TimeSpan.FromDays(365),
        AuditLogRetention: TimeSpan.FromDays(365));
}
