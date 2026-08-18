using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : Msg Trace 저장소
/// 역활 및 기능 : FlowActivityEvent 이력을 msg.Id 기준으로 누적해, 메시지 하나가 지나온 전체 경로를
/// on-demand 조회할 수 있게 하는 DI 싱글턴
///
/// (LK-04) 02번 설계 문서 7번 탭 카드5 "Runner 쪽: FlowActivityEvent가 발생할 때마다 msg.Id 기준으로
/// 누적"이 그대로 — <see cref="StatusBroadcaster"/>와 동일하게 <see cref="IEventBus"/>를 구독하는
/// "얇은 구독자"이지만, SignalR로 즉시 중계하는 대신 이 클래스 안에 쌓아두고 <see cref="MonitorHub"/>의
/// <c>GetMsgTrace(msgId)</c>(신규)가 조회할 때만 꺼내 돌려줍니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>구독 시점 — <c>Worker.ExecuteAsync</c></b>: <see cref="StatusBroadcaster"/>와 동일하게
/// <c>attachMonitor</c> 콜백에 함께 실려 <c>FlowDeployer.CreateEngineWithLogger</c>가 "진짜 새
/// <c>FlowEngine</c>을 만들 때만" 구독을 시작합니다 — 엔진 재사용 시 중복 구독되지 않습니다.</item>
/// <item><b>무제한 누적 방지</b>: 오래 켜둔 Runner가 처리한 메시지가 계속 쌓이면 메모리를 무한히
/// 먹으므로, <see cref="MaxTrackedMessages"/>(500)개를 넘는 새 msg.Id가 들어오면 가장 오래전에
/// 추적을 시작한 메시지부터 버립니다(<c>DebugSidebarView.MaxEntries</c>(200)·
/// <c>Editor.Core.Commands.CommandHistory</c>(50단계)와 동일한 "오래된 것부터 버리는 상한" 관례).</item>
/// <item><b>동시성</b>: <see cref="IEventBus.Subscribe{TEvent}"/> 핸들러가 어느 스레드에서 호출될지
/// 보장이 없고(발행자가 직접 동기 호출) <see cref="GetTrace"/>도 SignalR Hub 메서드 호출 스레드에서
/// 동시에 들어올 수 있어, 내부 딕셔너리·큐 접근을 전부 <c>lock</c>으로 감쌉니다(<c>RunnerTokenStore</c>와
/// 동일한 원칙).</item>
/// <item><b>반환값은 항상 복사본</b>: <see cref="GetTrace"/>가 내부 <see cref="MsgTrace"/> 인스턴스를
/// 그대로 반환하면, 호출자가 들고 있는 동안에도 새 <see cref="FlowActivityEvent"/>가 계속 그 인스턴스의
/// <see cref="MsgTrace.Steps"/>에 추가되어(참조 공유) SignalR 직렬화 도중 컬렉션이 바뀌는 경합이
/// 생길 수 있습니다 — 조회 시점의 스냅샷만 담은 새 인스턴스를 만들어 돌려줍니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Program.cs: builder.Services.AddSingleton&lt;MsgTraceStore&gt;();
/// // Worker.cs:  attachMonitor에 _statusBroadcaster.Subscribe(eventBus)와 함께 _msgTraceStore.Subscribe(eventBus)도 포함
/// // MonitorHub.cs: public Task&lt;MsgTrace?&gt; GetMsgTrace(string msgId) =&gt; Task.FromResult(_msgTraceStore.GetTrace(msgId));
/// </code>
/// </example>
public sealed class MsgTraceStore
{
    /// <summary>동시에 추적하는 최대 msg.Id 개수 — 위 클래스 remarks "무제한 누적 방지" 항목 참고.</summary>
    public const int MaxTrackedMessages = 500;

    private readonly object _lock = new();
    private readonly Dictionary<string, MsgTrace> _traces = new();
    private readonly Queue<string> _insertionOrder = new();

    /// <summary>
    /// <paramref name="eventBus"/>에 발행되는 <see cref="FlowActivityEvent"/>를 구독해 누적을
    /// 시작합니다. 반환된 <see cref="IDisposable"/>을 <c>Dispose()</c>하면 구독을 해제합니다
    /// (<see cref="IEventBus"/> XML 문서의 "구독은 반드시 해제" 규칙).
    /// </summary>
    public IDisposable Subscribe(IEventBus eventBus) => eventBus.Subscribe<FlowActivityEvent>(OnFlowActivity);

    /// <summary>
    /// <paramref name="e"/>를 <see cref="MsgTraceStep"/>으로 변환해 <see cref="MsgId"/> 기준 Trace에
    /// 추가합니다. 처음 보는 <see cref="FlowActivityEvent.MsgId"/>면 새 <see cref="MsgTrace"/>를 만들고,
    /// 그 결과 추적 중인 개수가 <see cref="MaxTrackedMessages"/>를 넘으면 가장 오래된 것부터 제거합니다.
    /// </summary>
    private void OnFlowActivity(FlowActivityEvent e)
    {
        lock (_lock)
        {
            if (!_traces.TryGetValue(e.MsgId, out var trace))
            {
                trace = new MsgTrace { MsgId = e.MsgId };
                _traces[e.MsgId] = trace;
                _insertionOrder.Enqueue(e.MsgId);

                while (_insertionOrder.Count > MaxTrackedMessages)
                {
                    var evictId = _insertionOrder.Dequeue();
                    _traces.Remove(evictId);
                }
            }

            trace.Steps.Add(new MsgTraceStep(e.FromNodeId, e.ToNodeId, e.At));
        }
    }

    /// <summary>
    /// <paramref name="msgId"/>로 지금까지 누적된 경로를 조회합니다. 한 번도 <see cref="FlowActivityEvent"/>가
    /// 발생하지 않았거나(예: 존재하지 않는 msg.Id) 상한을 넘어 이미 제거됐으면 <c>null</c>을 반환합니다.
    /// 반환값은 항상 호출 시점의 스냅샷 복사본입니다(위 클래스 remarks "반환값은 항상 복사본" 항목).
    /// </summary>
    public MsgTrace? GetTrace(string msgId)
    {
        lock (_lock)
        {
            if (!_traces.TryGetValue(msgId, out var trace))
            {
                return null;
            }

            var snapshot = new MsgTrace { MsgId = trace.MsgId };
            snapshot.Steps.AddRange(trace.Steps);
            return snapshot;
        }
    }
}
