using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 노드 컨텍스트
/// 역활 및 기능 : INodeContext의 정식 구현체(Local/Flow/Global/Env 스코프 + RouteAsync/SetStatus)
///
/// <see cref="INodeContext"/>의 정식 구현체입니다(RT-09b, 02번 문서 2번 탭 카드9 "정식 통합판" 중
/// 이번 Step 범위 — <c>Local</c>/<c>Flow</c>/<c>Global</c>/<c>Env</c> 4개 <see cref="ContextScope"/>와
/// <c>RouteAsync</c>/<c>SetStatus</c>만 우선 구현합니다. 카드9 원본의 <c>Shared</c>
/// (<c>SharedResourceManager</c>, <c>RT-10</c> 대기)/<c>Scheduler</c>/<c>Structure</c>
/// (<c>CT-04b</c> <c>IStructureService</c>는 이미 있지만 실 구현 연동은 별도 Step)는 아직 없습니다 —
/// 사용자 확인(2026-08 세션, "NodeContext(INodeContext 구현체)로 바로 진행")에 따라 새 클래스를 만들지
/// 않고, 그 Step들이 끝나면 이 클래스에 멤버만 추가할 예정입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><c>Local</c>/<c>Flow</c>/<c>Global</c>/<c>Env</c></b>: 전부 같은 <see cref="IContextStore"/>
/// (생성자로 주입, 기본은 <c>FlowEngine</c>이 갖고 있는 <see cref="InMemoryContextStore"/>)를 공유하되
/// scope 이름만 다르게 만든 <see cref="ContextScope"/> 4개입니다(<c>RT-09a</c> remarks에 이미 예고된
/// 조립 방식) — <c>node</c>(nodeId 단위)/<c>flow</c>(flowId 단위)/<c>global</c>(전역 단일)/
/// <c>env</c>(nodeId 단위, Subflow 인스턴스 환경변수 — 9번 탭 EnvSchema 실 연동은 향후 Step).</item>
/// <item><b><c>RouteAsync</c></b>: 이미 완성된 <see cref="FlowEngine.RouteAsync"/>로 그대로 위임합니다 —
/// <c>FlowEngine</c>의 옛 <c>NoOpNodeContext</c>가 하던 위임과 동일한 방식이라 동작 변화가 없습니다.</item>
/// <item><b><c>SetStatus</c></b>: <c>RT-07</c>로 이미 준비된 <see cref="IEventBus"/>에
/// <see cref="NodeStatusEvent"/>를 발행합니다(카드9 원본 <c>EventBus.Publish(new NodeStatusEvent(...))</c>와
/// 동일) — <c>FlowEngine</c>의 옛 <c>NoOpNodeContext.SetStatus</c>는 아무것도 하지 않았지만, 이제부터는
/// 실제로 이벤트가 발행됩니다(RT-07 EventBus 연동 전까지 계속 무동작이라던 FlowEngine 주석이 이 Step에서
/// 해소됨).</item>
/// <item><b>(NR-11) <c>Debug</c></b>: <see cref="SetStatus"/>와 동일한 <see cref="IEventBus"/>에
/// <c>DebugMessageEvent</c>를 발행합니다 — Debug 노드가 <c>ctx.Debug(Name, msg.ToJson())</c>를 호출하면
/// 이 컨텍스트가 이미 아는 <see cref="_nodeId"/>와 함께 이벤트로 감싸 발행합니다(<c>INodeContext.Debug</c>
/// XML 문서 참고).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var store = new InMemoryContextStore();
/// var eventBus = new EventBusAdapter();
/// var engine = new FlowEngine(registry, store, eventBus);
/// var ctx = new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");
///
/// ctx.Local.Set("count", 1);      // scope="node", scopeId="n1" — 이 노드만 봄
/// ctx.Flow.Set("shared", "a");    // scope="flow", scopeId="f1"  — 같은 탭 노드끼리 공유
/// ctx.Global.Set("total", 100);   // scope="global", scopeId=""  — 전체 공유
/// ctx.Env.Get&lt;string&gt;("SITE_ID");   // scope="env", scopeId="n1"
///
/// var sub = eventBus.Subscribe&lt;NodeStatusEvent&gt;(e =&gt; Console.WriteLine(e.Text));
/// ctx.SetStatus("green", "dot", "연결됨");   // NodeStatusEvent 발행 → 위 구독자가 받음
/// </code>
/// </example>
public sealed class NodeContext : INodeContext
{
    private readonly FlowEngine _engine;
    private readonly IEventBus _eventBus;
    private readonly string _nodeId;

    /// <summary>이 노드 하나만의 변수 스코프입니다(scope="node", scopeId=nodeId) — 다른 노드와 절대 섞이지 않습니다.</summary>
    public ContextScope Local { get; }

    /// <summary>이 노드가 속한 탭(Flow) 전체가 공유하는 변수 스코프입니다(scope="flow", scopeId=flowId). (NR-04) <see cref="IContextScope"/>로 타입 변경 — <see cref="INodeContext.Flow"/> 인터페이스 멤버를 그대로 구현.</summary>
    public IContextScope Flow { get; }

    /// <summary>모든 탭·노드가 함께 공유하는 전역 변수 스코프입니다(scope="global", scopeId=""). (NR-04) <see cref="IContextScope"/>로 타입 변경 — <see cref="INodeContext.Global"/> 인터페이스 멤버를 그대로 구현.</summary>
    public IContextScope Global { get; }

    /// <summary>
    /// Subflow 인스턴스 환경변수 스코프입니다(scope="env", scopeId=nodeId, 9번 탭 EnvSchema 연동은
    /// 향후 Step — 지금은 <see cref="Local"/>과 마찬가지로 <see cref="IContextStore"/> 저장/조회만 가능).
    /// </summary>
    public ContextScope Env { get; }

    /// <summary>
    /// <paramref name="engine"/>(RouteAsync 위임 대상)·<paramref name="eventBus"/>(SetStatus가 발행할
    /// 대상)·<paramref name="store"/>(4개 스코프가 공유할 저장소)를 받아 특정 <paramref name="flowId"/>/
    /// <paramref name="nodeId"/> 노드 전용 Context를 만듭니다.
    /// </summary>
    public NodeContext(FlowEngine engine, IEventBus eventBus, IContextStore store, string flowId, string nodeId)
    {
        _engine = engine;
        _eventBus = eventBus;
        _nodeId = nodeId;

        Local = new ContextScope(store, "node", nodeId);
        Flow = new ContextScope(store, "flow", flowId);
        Global = new ContextScope(store, "global", string.Empty);
        Env = new ContextScope(store, "env", nodeId);
    }

    /// <inheritdoc/>
    public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
        _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);

    /// <inheritdoc/>
    public void SetStatus(string fill, string shape, string text) =>
        _eventBus.Publish(new NodeStatusEvent(_nodeId, fill, shape, text, DateTime.UtcNow));

    /// <inheritdoc/>
    public void Debug(string nodeName, string msgJson) =>
        _eventBus.Publish(new DebugMessageEvent(_nodeId, nodeName, msgJson, DateTime.UtcNow));
}
