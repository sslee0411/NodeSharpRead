namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 클럭 드리프트 단계
/// 역활 및 기능 : ClockDriftMonitor가 측정한 시각 오차(초)를 Ok/Warning/Critical 3단계로 분류하는 값
///
/// (RN-05a) <see cref="ClockDriftMonitor.CheckAsync"/>가 읽은 오프셋(초 단위, 로컬 시계가 NTP
/// 기준 대비 얼마나 벗어났는지)을 사람이 바로 이해할 수 있는 3단계로 나눈 값입니다. 절대값
/// 기준 1초 미만은 <see cref="Ok"/>, 5초 미만은 <see cref="Warning"/>(/health에 경고 배지),
/// 그 이상은 <see cref="Critical"/>(감사 로그 자동 기록 대상)로 분류합니다.
/// 설계 근거: 02번 문서 7번 탭 카드12.
/// </summary>
public enum ClockDriftLevel
{
    /// <summary>오프셋 절대값이 1초 미만 — 정상.</summary>
    Ok,

    /// <summary>오프셋 절대값이 1초 이상 5초 미만 — /health에 경고 배지로 표시.</summary>
    Warning,

    /// <summary>오프셋 절대값이 5초 이상 — 다중 Runner 환경에서 시각 비교가 신뢰할 수 없는 수준.</summary>
    Critical
}
