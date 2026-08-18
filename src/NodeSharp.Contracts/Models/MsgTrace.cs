namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : Msg Trace 구간
/// 역활 및 기능 : 메시지 하나가 와이어 하나를 건너간 사건 1건(출발 노드→도착 노드, 시각)을 담는 불변 레코드
///
/// (LK-04) 02번 설계 문서 7번 탭 카드5 <c>MsgTraceStep</c>이 그대로 — <see cref="Events.FlowActivityEvent"/>
/// 하나가 <see cref="Models.MsgTrace"/>의 <see cref="MsgTrace.Steps"/>에 그대로 누적될 때 이 타입으로
/// 저장됩니다(<c>NodeSharp.Runner.Core.MsgTraceStore</c> 참고).
/// </summary>
/// <param name="FromNodeId">출발 노드 Id(<see cref="Events.FlowActivityEvent.FromNodeId"/>와 동일).</param>
/// <param name="ToNodeId">도착 노드 Id(<see cref="Events.FlowActivityEvent.ToNodeId"/>와 동일).</param>
/// <param name="At">이 구간을 지나간 시각(UTC).</param>
public sealed record MsgTraceStep(string FromNodeId, string ToNodeId, DateTime At);

/// <summary>
/// Class명 : Msg Trace
/// 역활 및 기능 : 메시지 하나(<see cref="MsgId"/> 기준)가 지금까지 거쳐온 노드 경로 전체를 시간순으로 담는 컨테이너
///
/// (LK-04) 02번 설계 문서 7번 탭 카드5 <c>MsgTrace</c>가 그대로 — 캔버스가 복잡해지면(노드 20개 이상)
/// "이 에러가 어느 msg 때문에, 어느 경로로 왔는지" 눈으로 따라가기 어렵다는 문제(개발 지침 1·4번,
/// 근본 원인 분석 용이성)를 해결하기 위해, <c>MsgId</c> 기준으로 <see cref="Events.FlowActivityEvent"/>
/// 이력을 모아둡니다. 에러 노드를 클릭했을 때 "출발지(Inject) → 거쳐온 노드들 → 에러 노드"를 한 번에
/// 볼 수 있게 하는 것이 목표입니다.
/// </summary>
/// <remarks>
/// <c>NodeSharp.Runner.Core.MsgTraceStore</c>가 <see cref="Events.FlowActivityEvent"/>를 구독해 매
/// 이벤트마다 이 타입 인스턴스를 <see cref="MsgId"/> 기준으로 찾거나 새로 만들어 <see cref="Steps"/>에
/// 추가합니다. <c>MonitorHub.GetMsgTrace(msgId)</c>(신규, LK-04)를 통해 Editor가 on-demand로
/// 조회합니다 — <see cref="Events.NodeErrorEvent"/> 자체에는 실어 보내지 않습니다(클래스 자체 문서
/// "왜 나눴는지" 참고).
/// </remarks>
/// <example>
/// <code>
/// // Runner 쪽: FlowActivityEvent가 발생할 때마다 msg.Id 기준으로 누적(MsgTraceStore 내부)
/// var trace = store.GetOrAdd(e.MsgId);
/// trace.Steps.Add(new MsgTraceStep(e.FromNodeId, e.ToNodeId, e.At));
///
/// // Editor 쪽: 에러 노드를 클릭하면(또는 NodeErrorEvent 수신 직후 자동으로) 해당 msg.Id의 Trace를
/// // 요청해 타임라인으로 표시
/// //   [Inject #1] 10:32:01.120
/// //     └▶ [Function "단위변환"] 10:32:01.121  (1ms 소요)
/// //          └▶ [Switch "임계값체크"] 10:32:01.123  (2ms 소요)
/// //               └▶ [HttpRequest "서버전송"] 10:32:01.980  ★ 여기서 에러 (857ms 소요, 타임아웃)
/// </code>
/// </example>
public sealed class MsgTrace
{
    /// <summary>이 Trace가 추적하는 메시지의 고유 식별자(<c>Msg.Id</c>).</summary>
    public string MsgId { get; init; } = default!;

    /// <summary>이 메시지가 지나온 구간을 시간순(발생한 순서 그대로 <see cref="MsgTraceStore"/>가 추가)으로 담은 목록.</summary>
    public List<MsgTraceStep> Steps { get; } = new();
}
