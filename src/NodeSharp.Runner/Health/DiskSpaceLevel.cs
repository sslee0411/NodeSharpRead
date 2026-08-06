namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 디스크 여유 공간 단계
/// 역활 및 기능 : DiskSpaceMonitor가 측정한 드라이브 여유 공간 비율(%)을 Ok/Warning/Critical 3단계로 분류하는 값
///
/// (RN-05b-a) <see cref="DiskSpaceMonitor.Check"/>가 읽은 여유 공간 비율(%, 전체 용량 대비 남은
/// 공간)을 사람이 바로 이해할 수 있는 3단계로 나눈 값입니다. 20% 초과는 <see cref="Ok"/>, 10%
/// 초과는 <see cref="Warning"/>(/health에 경고 배지 + 감사 로그 1회 기록), 그 이하는
/// <see cref="Critical"/>(/health 위험 배지 — Retention 정리 강제 실행 연동은 RetentionSweeper가
/// 준비된 뒤 RN-05b-b에서 이어집니다)로 분류합니다.
/// 설계 근거: 02번 문서 7번 탭 카드13.
/// </summary>
public enum DiskSpaceLevel
{
    /// <summary>여유 공간 비율이 20% 초과 — 정상.</summary>
    Ok,

    /// <summary>여유 공간 비율이 10% 초과 20% 이하 — /health에 경고 배지로 표시.</summary>
    Warning,

    /// <summary>여유 공간 비율이 10% 이하 — Historian 기록·flows.json 저장까지 실패할 수 있는 위험 수준.</summary>
    Critical
}
