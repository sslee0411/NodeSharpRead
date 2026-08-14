using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 수동 트리거 계약
/// 역활 및 기능 : 입력 포트 없이 외부 신호(캔버스 클릭 등)로만 동작을 시작하는 소스 노드가 선택적으로 구현하는 계약
///
/// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거하는 기능") <see cref="IFlowNode"/>는
/// <see cref="IFlowNode.OnInputAsync"/>로만 동작이 시작되는 노드를 전제하는데, <c>InjectNode</c>(NR-03a)
/// 처럼 입력 포트가 0개인 소스 노드는 애초에 <c>OnInputAsync</c>가 호출될 방법이 없어 이 계약과는 별개로
/// 자기 고유의 <c>TriggerAsync</c> 공개 메서드를 노출해왔습니다. <c>NodeSharp.Runtime.FlowEngine</c>이
/// (SignalR을 거쳐 Editor 캔버스가 보낸) "이 노드를 지금 수동으로 발동시켜"라는 요청을 처리하려면, 어떤
/// 구체 노드 타입이 이 능력을 가졌는지 <b>일반적인 방법으로</b> 판별해야 합니다 — 그렇다고 모든 노드가
/// 구현하는 <see cref="IFlowNode"/> 자체에 이 멤버를 추가하면(NR-04/NR-11 선례처럼 기존 구현체 전부를
/// 고쳐야 함) 대부분의 노드(입력 포트가 있는 Function/Switch/Debug 등)에는 의미 없는 멤버를 강제하게
/// 됩니다. 그래서 이 능력만 별도 인터페이스로 분리해, <c>InjectNode</c>처럼 실제로 수동 트리거가
/// 의미 있는 노드만 선택적으로 구현하도록 했습니다(Node-RED의 <c>node.on('input', ...)</c>과 별개로
/// 존재하는 <c>button</c> 설정과 대응하는 개념).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>사용처</b>: <c>NodeSharp.Runtime.FlowEngine.TriggerManualAsync(nodeId, payload, ct)</c>가
/// 배포된 노드 인스턴스를 <c>is IManuallyTriggerable</c>로 패턴 매칭해, 구현하는 노드에만
/// <see cref="TriggerAsync"/>를 호출합니다 — <c>FlowEngine</c>은 <c>InjectNode</c> 같은 구체 타입을
/// 전혀 몰라도 됩니다(플러그인 아키텍처 원칙, <c>NodeSharp.Runtime</c>이 <c>nodes\*</c> 프로젝트를
/// 참조하지 않는 기존 규칙과 동일).</item>
/// <item><b>Editor 쪽 신호</b>: 어떤 노드 타입이 이 인터페이스를 구현하는지는 <see cref="INodeTypeDescriptor.SupportsManualTrigger"/>
/// (기본값 <c>false</c>)로 Editor에 미리 알립니다 — Editor는 실제 <see cref="IFlowNode"/> 인스턴스를
/// 갖지 않으므로(캔버스는 메타데이터만 다룸) 인스턴스에 <c>is</c> 검사를 할 수 없어, 노드 타입을
/// 만드는 쪽이 두 곳(이 인터페이스 구현 여부·<see cref="INodeTypeDescriptor.SupportsManualTrigger"/>
/// 값)을 일치시켜야 합니다.</item>
/// <item><b>페이로드</b>: <paramref name="payload"/>는 <c>InjectNode.TriggerAsync</c>가 이미 쓰던
/// 것과 동일한 형태(값 그대로 <see cref="Msg.Payload"/>가 됨)입니다 — 지금은 Editor가 항상
/// <c>null</c>을 보내고(노드 속성에 저장된 기본 payload는 노드 인스턴스 자신이 이미 알고 있음),
/// 향후 "클릭 시 payload를 직접 입력" UI가 생기면 값을 채워 넘기도록 확장할 수 있습니다.</item>
/// </list>
/// </remarks>
public interface IManuallyTriggerable
{
    /// <summary>
    /// 새 <see cref="Msg"/>를 만들어 이 노드의 출력 포트로 즉시 전달합니다 — 정확히 몇 번 포트로 어떻게
    /// 전달할지는 구현 노드마다 다릅니다(<c>InjectNode</c>는 항상 0번 출력).
    /// </summary>
    Task TriggerAsync(object? payload, INodeContext ctx, CancellationToken ct);
}
