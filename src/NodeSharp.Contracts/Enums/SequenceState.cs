namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : 시퀀스 상태
/// 역활 및 기능 : SequenceExecutor가 시퀀스를 실행하는 동안 가질 수 있는 상태를 나타내는 열거형
///
/// <c>SequenceExecutor</c>(<c>SQ-01</c>)가 시퀀스 하나를 실행하는 동안 가질 수 있는 상태입니다.
/// <see cref="Events.SequenceStepChangedEvent"/>가 단계 전환마다 이 값을 함께 실어 발행합니다.
/// 설계 근거: 02번 문서 11번 탭 카드 5(Sequence 설계).
/// </summary>
/// <example>
/// <code>
/// // SequenceExecutor.State는 시작 시 Idle → 실행 시작 시 Running으로 전이
/// executor.State = SequenceState.Running;
///
/// // 알람 자동 안전정지(AlarmRaisedEvent 구독)나 사용자 AbortAsync() 호출 시 Faulted로 전이
/// if (alarm.Level == AlarmLevel.HH) executor.State = SequenceState.Faulted;
///
/// // Dashboard의 UiSequenceStatusNode가 이 값을 그대로 표시해 운영자가 진행 상태를 한눈에 확인
/// </code>
/// </example>
public enum SequenceState
{
    /// <summary>시작 전 대기 상태.</summary>
    Idle,

    /// <summary>단계를 순차 실행 중.</summary>
    Running,

    /// <summary>사용자가 <c>PauseAsync()</c>를 호출해 일시 정지된 상태.</summary>
    Paused,

    /// <summary>실패·타임아웃·알람 자동 안전정지 등으로 비정상 종료된 상태.</summary>
    Faulted,

    /// <summary>모든 단계가 정상적으로 끝난 상태.</summary>
    Completed
}
