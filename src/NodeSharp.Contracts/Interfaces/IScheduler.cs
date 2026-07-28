namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// 주기적/예약 실행이 필요한 모든 곳(Inject 노드의 Interval 트리거, DeviceMapPoller 배치 폴링,
/// RetentionSweeper 등)이 공통으로 의존하는 스케줄링 계약입니다. 구현체는
/// <c>lssLib.Messaging.AsyncScheduler</c>를 포팅한 <c>AsyncSchedulerAdapter</c>(NodeSharp.Runtime)이며,
/// "while-true 폴링 금지"(공통 규칙 ③)를 지키기 위해 이 인터페이스를 반드시 거칩니다.
/// 설계 근거: 02번 문서 6번 탭 카드 5.
/// </summary>
/// <remarks>
/// <c>ownerId</c>는 등록한 작업을 나중에 <see cref="Unschedule"/>로 취소하기 위한 키입니다 — 노드의
/// <c>OnCloseAsync</c>에서 반드시 호출해야 재배포 시 이전 스케줄이 중복 등록되지 않습니다(공통 규칙 ②와
/// 같은 취지, 대상이 이벤트 구독이 아니라 예약 작업이라는 점만 다릅니다).
/// </remarks>
/// <example>
/// <code>
/// // 1) Inject 노드의 주기(Interval) 트리거 — 5초마다 실행
/// scheduler.SchedulePeriodic(nodeId, TimeSpan.FromSeconds(5), async () =>
/// {
///     await ctx.RouteAsync(nodeId, 0, new Msg { Payload = DateTime.UtcNow }, CancellationToken.None);
/// });
///
/// // 2) DeviceMapPoller — cron 표현식으로 매시 정각 집계 실행
/// scheduler.ScheduleCron("tag-aggregation-hourly", "0 0 * * * *", async () => await ComputeHourlyAsync());
///
/// // 3) 노드 종료 시 반드시 해제 — 하지 않으면 재배포마다 같은 작업이 중복 등록됨
/// public Task OnCloseAsync(INodeContext ctx) { scheduler.Unschedule(nodeId); return Task.CompletedTask; }
/// </code>
/// </example>
public interface IScheduler
{
    /// <summary>지정한 간격마다 <paramref name="callback"/>을 반복 실행합니다(예: Inject 노드의 Interval 트리거).</summary>
    void SchedulePeriodic(string ownerId, TimeSpan interval, Func<Task> callback);

    /// <summary>Cron 표현식이 가리키는 시각마다 <paramref name="callback"/>을 실행합니다(예: RetentionSweeper의 매일 새벽 배치).</summary>
    void ScheduleCron(string ownerId, string cronExpression, Func<Task> callback);

    /// <summary><paramref name="ownerId"/>로 등록된 모든 예약 작업을 취소합니다. 노드 <c>OnCloseAsync</c>·재배포 시 반드시 호출해야 합니다.</summary>
    void Unschedule(string ownerId);
}
