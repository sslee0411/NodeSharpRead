namespace NodeSharp.Util.Messaging;

/// <summary>
/// Class명 : 예약 설정
/// 역활 및 기능 : AsyncScheduler.Schedule에 넘기는 세밀한 예약 설정
///
/// <see cref="AsyncScheduler.Schedule"/>에 넘기는 세밀한 예약 설정입니다. lssLib.Messaging.AsyncScheduler
/// 원본을 구조·이름 그대로 포팅(복사)했습니다 — <c>D:\lssLib</c>를 직접 참조하지 않고 같은 동작을 하는
/// 코드를 NodeSharp.Util로 옮겨왔습니다(포팅 정책, LL-00).
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서.
/// </summary>
/// <example>
/// <code>
/// var options = new ScheduleOptions
/// {
///     Name = "SensorPoll",
///     InitialDelay = TimeSpan.FromSeconds(5),
///     Interval = TimeSpan.FromSeconds(30),
///     MaxRuns = 100,           // 100회 실행 후 자동으로 멈춤
///     ContinueOnError = true,  // 콜백이 예외를 던져도 다음 회차는 계속 실행
/// };
/// AsyncScheduler.Instance.Schedule(async ct => await CheckAsync(ct), options);
/// </code>
/// </example>
public sealed class ScheduleOptions
{
    /// <summary>이 작업을 구분하는 이름입니다. 로그·조회용이며, 같은 이름을 여러 번 써도 됩니다(중복 검사 없음).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>맨 처음 실행까지 기다리는 시간입니다. <see cref="TimeSpan.Zero"/>면 바로 시작합니다.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 반복 실행 간격입니다. <see cref="TimeSpan.Zero"/>(0)이면 <see cref="AsyncScheduler.ScheduleOnce"/>처럼
    /// 딱 한 번만 실행하고 끝납니다.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.Zero;

    /// <summary>최대 몇 번까지 실행할지입니다. 0이면 <see cref="ScheduledTask.Cancel"/>을 호출할 때까지 무제한 반복합니다.</summary>
    public int MaxRuns { get; init; }

    /// <summary>콜백이 예외를 던져도 다음 회차를 계속 실행할지입니다. <c>false</c>면 예외가 나는 순간 반복을 멈춥니다.</summary>
    public bool ContinueOnError { get; init; } = true;
}
