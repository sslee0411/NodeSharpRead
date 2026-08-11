using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 노드 컨텍스트 계약
/// 역활 및 기능 : IFlowNode가 실행 중 사용하는 메시지 전달·상태 표시 기능을 노출하는 계약
///
/// <see cref="IFlowNode"/>가 실행 중 사용하는 기능(메시지 전달, 상태 표시 등)을 노출하는
/// 인터페이스입니다. NodeSharp.Runtime의 구체 클래스 <c>NodeContext</c>가 이를 구현하며,
/// Contracts는 이 인터페이스만 알면 되므로 Runtime을 참조하지 않아도 됩니다.
/// 설계 근거: 02번 문서 2번 탭 카드 1(v1.57 추가) · 카드 9(NodeContext 구현체).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>왜 필요한가</b>: <c>NodeContext</c>(구체 클래스)는 <c>FlowEngine</c>/<c>EventBus</c>/
/// <c>SharedResourceManager</c> 등 Runtime 전용 타입에 의존해 Runtime 소속이어야 합니다. 그런데
/// <see cref="IFlowNode"/>는 Contracts 소속이라 구체 <c>NodeContext</c>를 그대로 참조하면
/// Runtime↔Contracts 순환 참조가 됩니다 — 이 인터페이스가 그 경계를 끊습니다.</item>
/// <item><b>점진적 확장</b>: 지금은 Phase 4 메시지 파이프라인과 상태 표시, <see cref="Flow"/>/
/// <see cref="Global"/> Context 접근(NR-04)에 필요한 멤버만 있습니다. <c>RT-07</c>(EventBus 구독)·
/// <c>RT-08</c>(Scheduler)·<c>RT-10</c>(SharedResourceManager)·<c>CT-04b</c>(Structure)가 구현될
/// 때마다 이 인터페이스에 필요한 멤버가 추가됩니다(02번 문서 2번 탭 카드 1·9와 함께 갱신).</item>
/// <item><b>(NR-04) <see cref="Flow"/>/<see cref="Global"/> 추가</b>: RT-09a/b/c가 이미 완성한
/// <c>IContextStore</c>/<c>ContextScope</c>/<c>NodeContext</c>는 Flow/Global/Env 접근자를 구현체
/// <c>NodeContext</c>(Runtime)에만 노출하고 있었습니다 — Switch 노드(<c>nodes\*</c>, Contracts+Util만
/// 참조)가 <see cref="TypedValue"/>의 FlowContext/GlobalContext Source를 읽으려면
/// <see cref="INodeContext"/> 자체에 접근자가 있어야 하는데 없던 공백이라, <see cref="IContextScope"/>를
/// 신설해 <see cref="Flow"/>/<see cref="Global"/> 2개만 우선 노출합니다(사용자 확인, 2026-08 세션).
/// <c>Local</c>(node 스코프)/<c>Env</c>는 아직 이 인터페이스에 없습니다 — <c>Env</c>는 <c>NR-10b</c>
/// (환경변수 병합)가 실제 값을 채우기 전까지는 노출해도 항상 빈 스코프라 의미가 없어 함께 미룹니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 다음 노드로 메시지 전달(Fan-out의 단일 분기) — IFlowNode.OnInputAsync 안에서 사용
/// public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =&gt;
///     ctx.RouteAsync(Id, outputPort: 0, msg, ct);
///
/// // 2) 기존 방식(문자열, Node-RED와 동일한 자유 문자열)
/// ctx.SetStatus("green", "dot", "연결됨");
///
/// // 3) 신규 방식 — NodeStatusLevel Enum으로 오타 없이(내부적으로 위 문자열 오버로드에 위임)
/// ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");
/// </code>
/// </example>
public interface INodeContext
{
    /// <summary>
    /// <paramref name="sourceNodeId"/>의 <paramref name="outputPort"/>번 출력 포트에 연결된 다음
    /// 노드(들)로 <paramref name="msg"/>를 전달합니다. Fan-out(여러 와이어) 처리는 이 메서드를
    /// 구현하는 <c>FlowEngine</c> 쪽 책임입니다.
    /// </summary>
    Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct);

    /// <summary>노드 하단 상태 점을 갱신합니다. Node-RED의 <c>this.status({fill, shape, text})</c>와 동일한 자유 문자열 방식입니다.</summary>
    void SetStatus(string fill, string shape, string text);

    /// <summary><see cref="NodeStatusLevel"/> Enum으로 오타 없이 상태를 지정하는 타입 세이프 오버로드입니다 — 기본 구현이 <see cref="SetStatus(string, string, string)"/>에 위임합니다.</summary>
    void SetStatus(NodeStatusLevel level, string shape, string text) =>
        SetStatus(level.ToString().ToLowerInvariant(), shape, text);

    /// <summary>
    /// (NR-04) 이 노드가 속한 Flow(탭) 전체가 공유하는 Context 스코프입니다(Node-RED의 <c>flow.get/set</c>에
    /// 대응). <see cref="TypedValueSource.FlowContext"/> Source를 가진 <see cref="TypedValue"/>를
    /// 해석할 때 씁니다.
    /// </summary>
    IContextScope Flow { get; }

    /// <summary>
    /// (NR-04) 모든 탭·노드가 함께 공유하는 전역 Context 스코프입니다(Node-RED의 <c>global.get/set</c>에
    /// 대응). <see cref="TypedValueSource.GlobalContext"/> Source를 가진 <see cref="TypedValue"/>를
    /// 해석할 때 씁니다.
    /// </summary>
    IContextScope Global { get; }
}
