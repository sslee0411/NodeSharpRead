namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 클럭 드리프트 상태
/// 역활 및 기능 : ClockDriftMonitor 한 번의 확인 결과(오프셋·단계·확인 시각)를 담는 불변 값
///
/// (RN-05a) <see cref="ClockDriftMonitor.CheckAsync"/> 호출 1회의 결과입니다. /health 응답의
/// ClockDrift 필드로 그대로 JSON 직렬화되어, 다중 Runner PC 간 시각이 얼마나 벗어나 있는지
/// 외부에서 확인할 수 있게 합니다.
/// 설계 근거: 02번 문서 7번 탭 카드12.
/// </summary>
/// <param name="OffsetSeconds">로컬 시계가 NTP 기준 대비 벗어난 초(부호 있음 — 양수면 로컬이 더 빠름).</param>
/// <param name="Level">OffsetSeconds를 기준으로 분류한 단계.</param>
/// <param name="CheckedAt">이 확인이 수행된 시각(UTC).</param>
public sealed record ClockDriftStatus(double OffsetSeconds, ClockDriftLevel Level, DateTime CheckedAt);
