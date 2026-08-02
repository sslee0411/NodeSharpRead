using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Events;

/// <summary>
/// Class명 : 위젯 값 갱신 이벤트
/// 역활 및 기능 : Dashboard 위젯 값이 갱신됐음을 Flow → 대시보드 방향으로 알리는 이벤트
///
/// Dashboard 위젯 값이 갱신됐음을 알리는 이벤트입니다(Flow → 대시보드 방향). 출력 전용 위젯
/// (<c>UiGaugeNode</c>/<c>UiChartNode</c>/<c>UiTextNode</c>)이 msg를 받을 때마다 발행하며, 웹(<c>/ui</c>)과
/// WPF 대시보드 양쪽이 같은 이벤트를 구독해 동일한 값을 렌더링합니다(듀얼 렌더링).
/// 설계 근거: 02번 문서 9번 탭 카드 11.
/// </summary>
/// <example>
/// <code>
/// // UiGaugeNode.OnInputAsync — 값을 받으면 위젯 갱신 이벤트를 발행하고, 다음 노드로도 그대로 전달(디버깅 체이닝 가능)
/// var value = Convert.ToDouble(msg.Payload);
/// eventBus.Publish(new WidgetValueUpdatedEvent(NodeId: Id, Value: value, At: DateTime.UtcNow));
///
/// // UiSequenceStatusNode도 같은 이벤트를 재사용 — Value에 시퀀스 진행 상태를 익명 타입으로 실어 보냄
/// eventBus.Publish(new WidgetValueUpdatedEvent(Id, new { e.CurrentStepId, e.State, e.ElapsedMs }, DateTime.UtcNow));
/// </code>
/// </example>
public sealed record WidgetValueUpdatedEvent(string NodeId, object? Value, DateTime At);

/// <summary>
/// Class명 : 위젯 조작 이벤트
/// 역활 및 기능 : 운영자의 Dashboard 위젯 조작을 대시보드 → Flow 방향으로 알리는 이벤트
///
/// 운영자가 Dashboard 위젯을 조작했음을 알리는 이벤트입니다(대시보드 → Flow 방향). 입력 겸용 위젯
/// (<c>UiButtonNode</c>/<c>UiSwitchNode</c>/<c>UiSliderNode</c>)이 이 이벤트를 구독해 조작이 발생하면
/// Flow의 새 msg로 변환합니다 — 버튼 클릭이 Inject 노드와 유사하게 Flow의 시작점이 됩니다.
/// 설계 근거: 02번 문서 9번 탭 카드 11.
/// </summary>
/// <example>
/// <code>
/// // 웹 대시보드: 버튼 클릭 시 hub.invoke("Interact", nodeId, value) → Runner가 아래 이벤트로 변환해 발행
/// eventBus.Publish(new WidgetInteractionEvent(NodeId: "btn-1", UserInput: true, At: DateTime.UtcNow));
///
/// // UiButtonNode.OnStartAsync — 자신의 NodeId로 온 조작만 걸러 Flow의 새 msg로 변환
/// eventBus.Subscribe&lt;WidgetInteractionEvent&gt;(e =&gt;
/// {
///     if (e.NodeId != Id) return;
///     var msg = new Msg(); msg.Payload = e.UserInput ?? true;
///     _ = ctx.RouteAsync(Id, 0, msg, ct);
/// });
/// </code>
/// </example>
public sealed record WidgetInteractionEvent(string NodeId, object? UserInput, DateTime At);

/// <summary>
/// Class명 : 시퀀스 단계 전환 이벤트
/// 역활 및 기능 : SequenceExecutor가 단계를 전환할 때마다 발행하는 이벤트
///
/// <c>SequenceExecutor</c>가 단계를 전환할 때마다 발행하는 이벤트입니다. <c>UiSequenceStatusNode</c>가
/// <see cref="SequenceId"/>로 필터링해 구독한 뒤 <see cref="WidgetValueUpdatedEvent"/>로 다시 감싸
/// Dashboard에 노출합니다 — Flow 노드가 아니라 시퀀스를 직접 구독하는 유일한 위젯입니다.
/// 설계 근거: 02번 문서 9번 탭 카드 12, 11번 탭 카드 5.
/// </summary>
/// <example>
/// <code>
/// eventBus.Publish(new SequenceStepChangedEvent(
///     SequenceId: "seq-1", CurrentStepId: "step-3", State: SequenceState.Running, ElapsedMs: 4200));
/// </code>
/// </example>
public sealed record SequenceStepChangedEvent(string SequenceId, string CurrentStepId, SequenceState State, long ElapsedMs);

/// <summary>
/// Class명 : 노드 완료 이벤트
/// 역활 및 기능 : FlowEngine이 노드의 OnInputAsync 처리를 완료할 때마다 발행하는 이벤트
///
/// <c>FlowEngine</c>이 노드의 <c>OnInputAsync</c> 처리를 완료할 때마다 발행하는 이벤트입니다.
/// <c>CompleteNode</c>가 <see cref="NodeId"/>로 필터링해 구독하며, Node-RED의 Complete 노드처럼
/// "어떤 노드가 끝났을 때"를 반드시 지정해야 합니다(Catch 노드의 "전체" 옵션과 달리 미지원).
/// 설계 근거: 02번 문서 9번 탭 카드 4.
/// </summary>
/// <remarks>
/// <see cref="HadOutput"/>은 <c>HttpRequestNode</c>처럼 "받자마자 다음 노드로 넘기고, 응답은 나중에
/// 콜백으로 처리"하는 노드를 위한 필드입니다 — 이런 노드는 콜백이 실제로 끝나는 시점에 선택적
/// 인터페이스(<c>INodeCompletable</c>)를 통해 <c>NotifyNodeComplete</c>를 한 번 더 호출합니다.
/// </remarks>
/// <example>
/// <code>
/// // FlowEngine.InvokeNodeAsync — 노드의 OnInputAsync가 실제로 끝난 직후 발행
/// await node.OnInputAsync(msg, ctx, ct);
/// eventBus.Publish(new NodeCompleteEvent(NodeId: node.Id, MsgId: msg.Id, HadOutput: pendingOutputCount &gt; before, At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record NodeCompleteEvent(string NodeId, string MsgId, bool HadOutput, DateTime At);
