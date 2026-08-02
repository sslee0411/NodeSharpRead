namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : 알람 레벨
/// 역활 및 기능 : 계층형 구조 설정의 태그 값 알람 임계값 4단계(HH/H/L/LL)를 나타내는 열거형
///
/// 계층형 구조 설정(8번 탭)에서 태그 값에 매길 수 있는 알람 임계값 4단계입니다. 산업 현장의
/// 일반적인 알람 표기법을 그대로 따릅니다: HH(High-High)·H(High)·L(Low)·LL(Low-Low).
/// <c>AlarmStateManager</c>가 태그 값이 각 임계값을 넘을 때 이 단계로 알람 상태를 전이시키고
/// Ack(확인)·Shelve(억제)를 관리합니다.
/// 설계 근거: 02번 문서 8번 탭 카드 11.
/// </summary>
/// <remarks>
/// Enum 정의 순서(HH, H, L, LL)는 심각도 순서가 아닙니다 — 실제로는 HH/LL(상/하한을 크게
/// 벗어난 긴급 상태)이 가장 심각하고, H/L(상/하한에 근접한 주의 상태)이 상대적으로 경미합니다.
/// 따라서 <c>(int)</c> 캐스팅 값으로 두 알람의 심각도를 비교하면 안 됩니다. 심각도 비교가
/// 필요하면 별도의 헬퍼 메서드를 통해야 합니다.
/// </remarks>
/// <example>
/// <code>
/// // 탱크 온도 태그에 4단계 임계값 지정
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
    LL
}
