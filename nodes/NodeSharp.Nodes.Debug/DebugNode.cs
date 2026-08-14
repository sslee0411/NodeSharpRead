using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Debug;

/// <summary>
/// Class명 : Debug 노드
/// 역활 및 기능 : 받은 msg를 DebugMessageEvent로 발행해 Editor 디버그 사이드바에 보여주는(그리고 선택적으로 다음 노드로도 전달하는) 노드
///
/// 03번 개발 Step맵 NR-11의 구현체입니다. <see cref="Msg"/>를 받으면 <see cref="INodeContext.Debug"/>로
/// <c>DebugMessageEvent</c>를 즉시 발행하고(02번 문서 7번 탭 카드2 <c>DebugMessageEvent</c>·카드9
/// <c>NodeContext.Debug</c>), <see cref="ToNext"/>가 켜져 있으면 그 msg를 그대로 0번 출력 포트로도
/// 전달합니다(NR-11 desc가 명시한 "다음 노드로도 전달할지" 옵션). Inject→Function→Switch→Debug 4개
/// 코어 노드 중 마지막으로, 이 노드가 갖춰지면서 Phase 7이 마무리됩니다.
/// 설계 근거: 02번 문서 5번 탭 카드1(msg 디버그/저장)·7번 탭 카드2(DebugMessageEvent), 03번 개발
/// Step맵 Phase 7 NR-11.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>이 Step의 실제 범위 — "발행"까지만, "화면 표시"는 범위 밖</b>: NR-11 desc는 "LK-02(SignalR
/// 모니터링)·EC-03(다음 노드로도 전달할지 옵션)까지 연결돼야 완성"이라고 명시합니다 — 즉 이 노드
/// 자신은 <c>DebugMessageEvent</c>를 발행하는 것까지만 책임지고, 그 이벤트가 실제로 Editor 우측
/// 디버그 사이드바에 그려지는 것은 <c>LK-01~04</c>(SignalR Hub·<c>EditorMonitorClient</c>, 전부
/// <c>⏳ 대기</c>)와 그 사이드바 자체를 만들 아직 배정되지 않은 Editor UI Step의 몫입니다. 완료 기준
/// 3개 중 ①"DebugMessageEvent가 발행되는지"·②"toNext On/Off에 따라 다운스트림 전달 여부가
/// 달라지는지"는 이 클래스만으로 xUnit에서 직접 검증 가능하지만, ③"Pause 상태에서는 사이드바가
/// 갱신되지 않다가 해제 후 다시 표시되는지"는 그 사이드바 자체가 아직 없어(디버그 사이드바 UI가
/// 만들어질 Step은 03번 Step맵에 아직 배정되지 않음, LK-02 계열과 함께 향후 결정) 이 Step에서 자동
/// 검증할 수 없습니다 — 02번 문서 7번 탭 카드2가 이미 "Runner 쪽 이벤트 발행 로직은 변경 없음, 순수
/// Editor UI 상태"라고 명시해 Pause 자체가 애초에 이 노드(Runtime)의 책임이 아님을 뒷받침합니다.</item>
/// <item><b><see cref="INodeContext.Debug"/> 신규</b>: <see cref="OnInputAsync"/>가 <c>DebugMessageEvent</c>를
/// 발행하려면 <c>IEventBus</c>가 필요한데, 이 노드는 <c>nodes\NodeSharp.Nodes.Debug</c>(Contracts만
/// 참조)에 있어 Runtime의 <c>IEventBus</c>를 직접 참조할 수 없습니다 — <c>SetStatus</c>가
/// <c>NodeStatusEvent</c> 발행을 감춘 것과 똑같은 이유로 <c>INodeContext</c>에 <c>Debug</c> 멤버를
/// 신규 추가했습니다(<c>INodeContext.cs</c> XML 문서의 NR-11 항목 참고).</item>
/// <item><b>발행이 먼저, 라우팅은 그다음</b>: <see cref="OnInputAsync"/>는 <see cref="ToNext"/> 값과
/// 무관하게 항상 먼저 <c>ctx.Debug(...)</c>를 호출합니다 — "다음 노드로 전달"을 끄더라도 디버그
/// 사이드바 표시(발행)까지 함께 꺼지면 안 되기 때문입니다(둘은 서로 독립된 관심사).</item>
/// <item><b>Clone 불필요</b>: <see cref="OnInputAsync"/>가 <c>ctx.RouteAsync</c>에 넘기는 <paramref name="msg"/>는
/// 별도로 <c>Clone()</c>하지 않습니다 — <c>FlowEngine.DispatchOneAsync</c>(RT-04a/b)가 각 대상 노드에
/// 전달하기 직전에 이미 <c>msg.Clone()</c>을 수행해 분기 간 데이터 격리를 보장하므로(<c>FunctionNode</c>와
/// 동일한 선례), 이 노드가 다시 Clone하면 중복입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var node = new DebugNode { Id = "n1", Name = "디버그", ToNext = false };
/// await node.OnStartAsync(ctx, CancellationToken.None);
/// await node.OnInputAsync(new Msg { Payload = 42 }, ctx, CancellationToken.None);
/// // ctx.Debug("디버그", "{\"payload\":42,...}")가 호출됨. ToNext=false라 다음 노드로는 전달되지 않음.
/// </code>
/// </example>
public sealed class DebugNode : IFlowNode
{
    /// <inheritdoc/>
    public string Id { get; init; } = default!;

    /// <inheritdoc/>
    public string Type => "debug";

    /// <inheritdoc/>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <summary>
    /// <see cref="ToNext"/>가 켜져 있을 때만 실제로 쓰이는 출력 포트입니다 — 꺼져 있어도 포트
    /// 자체는 항상 존재합니다(캔버스에서 미리 와이어를 연결해두고 나중에 토글만 켜는 것도 가능하게).
    /// </summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>
    /// <c>true</c>면 <see cref="INodeContext.Debug"/> 발행 후 msg를 0번 출력 포트로도 전달합니다.
    /// 기본값 <c>false</c>는 Node-RED Debug 노드의 실사용 관례(기본은 사이드바 표시만, 다음 노드로는
    /// 흘려보내지 않음)와 동일합니다.
    /// </summary>
    public bool ToNext { get; init; }

    /// <summary>구독·연결이 없어 아무 일도 하지 않습니다.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// <paramref name="msg"/>를 <c>ctx.Debug(Name, msg.ToJson())</c>로 항상 먼저 발행하고,
    /// <see cref="ToNext"/>가 켜져 있으면 이어서 0번 출력 포트로도 전달합니다.
    /// </summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
    {
        ctx.Debug(Name, msg.ToJson());
        return ToNext ? ctx.RouteAsync(Id, outputPort: 0, msg, ct) : Task.CompletedTask;
    }

    /// <summary>정리할 구독·연결이 없어 아무 일도 하지 않습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
}
