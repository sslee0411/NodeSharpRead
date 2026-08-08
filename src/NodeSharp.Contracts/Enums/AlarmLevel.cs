namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : 알람 레벨
/// 역활 및 기능 : 계층형 구조 설정의 태그 값 알람 임계값 4단계(HH/H/L/LL) + 특정값 일치/불일치
/// 2종(EQ/NE)을 나타내는 열거형
///
/// 계층형 구조 설정(8번 탭)에서 태그 값에 매길 수 있는 알람 종류입니다. 아날로그 태그(온도·압력
/// 등 연속값)는 산업 현장의 일반적인 임계값 표기법을 그대로 따릅니다: HH(High-High)·H(High)·
/// L(Low)·LL(Low-Low). 디지털/상태 태그(설비 상태 코드 등 이산값)는 특정값 비교 2종을 씁니다:
/// EQ(Equal, 특정값과 일치할 때 알람)·NE(NotEqual, 특정값과 다를 때—그 값 이외의 모든 값—알람,
/// ★ 사용자 요청으로 v2.50 신설). <c>AlarmStateManager</c>가 태그 값이 각 조건을 만족할 때 이
/// 단계로 알람 상태를 전이시키고 Ack(확인)·Shelve(억제)를 관리합니다.
/// 설계 근거: 02번 문서 8번 탭 카드 11.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Enum 정의 순서(HH, H, L, LL, EQ, NE)는 심각도 순서가 아닙니다 — 실제로는 HH/LL(상/하한을
/// 크게 벗어난 긴급 상태)이 가장 심각하고, H/L(상/하한에 근접한 주의 상태)이 상대적으로 경미합니다.
/// EQ/NE는 애초에 아날로그 임계값과 성격이 달라(이산값 일치/불일치 판정) HH~LL과 같은 축으로
/// 비교할 수 없습니다. 따라서 <c>(int)</c> 캐스팅 값으로 알람의 심각도를 비교하면 안 됩니다.
/// 심각도 비교가 필요하면 별도의 헬퍼 메서드를 통해야 합니다.</item>
/// <item><b>EQ/NE의 비교 대상값</b>: HH/H/L/LL이 각각 <see cref="Models.AlarmRuntimeInfo.HH"/> 등
/// 자기 전용 임계값 필드를 갖는 것과 동일하게, EQ/NE도 각각 <see cref="Models.AlarmRuntimeInfo.EQ"/>/
/// <see cref="Models.AlarmRuntimeInfo.NE"/> 필드에 담긴 값과 태그 값을 비교합니다(같은 태그에
/// EQ/NE를 동시에 서로 다른 비교값으로 설정할 수도 있음).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 아날로그 태그(탱크 온도) — 4단계 임계값 지정
/// var scale = new TagScale { HH = 95.0, H = 85.0, L = 10.0, LL = 0.0 };
///
/// // 값 갱신 시 임계값을 넘었는지 판정하는 전형적인 패턴
/// double temp = 96.2;
/// AlarmLevel? level = temp switch
/// {
///     _ when temp >= scale.HH => AlarmLevel.HH,   // 96.2 >= 95.0 → 긴급 정지 고려
///     _ when temp >= scale.H  => AlarmLevel.H,
///     _ when temp &lt;= scale.LL => AlarmLevel.LL,   // 동결 위험
///     _ when temp &lt;= scale.L  => AlarmLevel.L,
///     _ => null   // 정상 범위, 알람 없음
/// };
///
/// // 알람 발생 시 Dashboard·노드 상태 표시에 반영
/// if (level == AlarmLevel.HH)
///     ctx.SetStatus(NodeStatusLevel.Red, "dot", $"긴급 알람: {temp}°C");
///
/// // 2) 디지털/상태 태그(설비 상태 코드) — 특정값 일치/불일치 알람
/// var faultScale = new TagScale { EQ = 3.0 };       // 상태코드 3(고장) "일 때" 알람
/// var deviationScale = new TagScale { NE = 1.0 };   // 상태코드 1(정상 가동) "이 아닐 때" 알람
///
/// double statusCode = 3.0;
/// AlarmLevel? statusLevel = statusCode == faultScale.EQ ? AlarmLevel.EQ
///                          : statusCode != deviationScale.NE ? AlarmLevel.NE
///                          : null;
/// </code>
/// </example>
public enum AlarmLevel
{
    /// <summary>High-High — 상한을 크게 초과한 긴급 알람. 즉각 조치가 필요합니다.</summary>
    HH,

    /// <summary>High — 상한에 근접했거나 초과한 주의 알람. HH보다는 여유가 있습니다.</summary>
    H,

    /// <summary>Low — 하한에 근접했거나 미달한 주의 알람. LL보다는 여유가 있습니다.</summary>
    L,

    /// <summary>Low-Low — 하한을 크게 미달한 긴급 알람. 즉각 조치가 필요합니다.</summary>
    LL,

    /// <summary>
    /// Equal — 태그 값이 지정된 특정값과 일치할 때 발생하는 알람(★ 사용자 요청, v2.50 신설).
    /// 이산/상태 태그(예: 설비 상태 코드가 "고장" 값과 일치)에 주로 사용합니다.
    /// </summary>
    EQ,

    /// <summary>
    /// NotEqual — 태그 값이 지정된 특정값과 다를 때(그 값 이외의 모든 값) 발생하는 알람
    /// (★ 사용자 요청, v2.50 신설). 이산/상태 태그(예: 상태 코드가 "정상 가동" 값이 아닌 모든
    /// 경우)에 주로 사용합니다.
    /// </summary>
    NE
}
