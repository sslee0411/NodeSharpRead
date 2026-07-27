namespace NodeSharp.Contracts.Enums;

/// <summary>
/// 계층형 구조 설정(8번 탭)의 태그 알람 임계값 4단계입니다. 산업 현장의 일반적인 알람 표기법
/// (HH=High-High, H=High, L=Low, LL=Low-Low)을 그대로 따릅니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 8번 탭(계층형 구조 설정 &amp; PLC 데이터 계층) 카드 11
/// — <c>AlarmStateManager</c>가 태그 값이 각 임계값을 넘을 때 이 4단계 중 하나의 알람 상태로
/// 전이시키고, Ack(확인)·Shelve(억제, 최대 8시간)를 관리합니다(ED-D07a/ED-D07b Step에서 구현).
/// </para>
/// <para>
/// 값의 크기 순서는 <b>정의 순서(HH < H < L < LL의 심각도가 아니라, HH/LL이 더 심각하고
/// H/L이 상대적으로 경미)</b>이므로, 단순히 <c>(int)</c> 캐스팅으로 심각도를 비교하면
/// 안 됩니다 — 심각도 비교가 필요하면 별도의 헬퍼(향후 Step에서 필요 시 추가)를 통해야 합니다.
/// 이 Step(CT-01a)에서는 Enum 정의만 하고, 심각도 비교 로직은 실제로 필요해지는
/// <c>ED-D07a</c>에서 판단합니다(지금 미리 만들지 않음 — 과설계 방지).
/// </para>
/// </remarks>
/// <example>
/// 태그 스케일 설정에서 알람 임계값을 지정하고, 값 갱신 시 알람 상태를 판정하는 예
/// (실제 <c>AlarmStateManager</c> 구현은 ED-D07a에서 진행 — 아래는 이 Enum의 쓰임을 보여주는 예시일 뿐):
/// <code>
/// // 태그 스케일 설정 예: 탱크 온도 태그에 4단계 임계값을 지정
/// var scale = new TagScale
/// {
///     HH = 95.0,   // 95도 이상이면 AlarmLevel.HH (긴급 정지 고려)
///     H  = 85.0,   // 85도 이상이면 AlarmLevel.H  (주의)
///     L  = 10.0,   // 10도 이하이면 AlarmLevel.L  (주의, 저온)
///     LL = 0.0     // 0도 이하이면 AlarmLevel.LL  (긴급, 동결 위험)
/// };
///
/// // 알람이 발생하면(ED-D07a 구현 예정) Dashboard에도 표시(v1.19 UiSequenceStatusNode와 유사한 패턴)
/// // ctx.SetStatus(NodeStatusLevel.Red, "dot", $"알람: {AlarmLevel.HH}");
/// </code>
/// </example>
public enum AlarmLevel
{
    /// <summary>High-High — 상한 긴급 알람. 즉각적인 조치가 필요한 가장 심각한 상한 초과 상태입니다.</summary>
    HH,

    /// <summary>High — 상한 주의 알람. 상한에 근접했거나 초과했지만 HH보다는 여유가 있는 상태입니다.</summary>
    H,

    /// <summary>Low — 하한 주의 알람. 하한에 근접했거나 미달했지만 LL보다는 여유가 있는 상태입니다.</summary>
    L,

    /// <summary>Low-Low — 하한 긴급 알람. 즉각적인 조치가 필요한 가장 심각한 하한 미달 상태입니다.</summary>
    LL
}
