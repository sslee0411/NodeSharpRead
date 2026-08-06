namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 디스크 여유 공간 상태
/// 역활 및 기능 : DiskSpaceMonitor 한 번의 확인 결과(여유 바이트·여유 비율·단계·확인 시각)를 담는 불변 값
///
/// (RN-05b-a) <see cref="DiskSpaceMonitor.Check"/> 호출 1회의 결과입니다. /health 응답의
/// DiskSpace 필드로 그대로 JSON 직렬화되어, flows.json/historian.db가 저장되는 드라이브의 여유
/// 공간이 얼마나 남았는지 외부에서 확인할 수 있게 합니다.
/// 설계 근거: 02번 문서 7번 탭 카드13.
/// </summary>
/// <param name="AvailableFreeBytes">드라이브에 남은 여유 공간(바이트).</param>
/// <param name="FreePercent">전체 용량 대비 여유 공간 비율(%).</param>
/// <param name="Level">FreePercent를 기준으로 분류한 단계.</param>
/// <param name="CheckedAt">이 확인이 수행된 시각(UTC).</param>
public sealed record DiskSpaceStatus(long AvailableFreeBytes, double FreePercent, DiskSpaceLevel Level, DateTime CheckedAt);
