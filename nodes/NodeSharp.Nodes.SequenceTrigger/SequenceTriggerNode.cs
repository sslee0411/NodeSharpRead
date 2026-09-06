using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.SequenceTrigger;

/// <summary>
/// Class명 : 시퀀스 트리거 노드
/// 역활 및 기능 : 캔버스에서 특정 SequenceDefinition을 가리키는 자리표시자 노드 — 더블클릭하면 Sequence Editor 창으로 편도 이동하는 진입점
///
/// (SQ-03) 사용자 확인(2026-09-03 세션, AskUserQuestion — "SequenceTriggerNode 범위") 결과 "에디터
/// 전용 최소 노드로 지금 만들기(권장)" 선택에 따라 구현한 최소 노드입니다. 이 클래스가 담당하는 것은
/// "<see cref="SequenceId"/>로 어떤 시퀀스를 가리키는지"와 "그 값을 편집·역참조할 수 있게 PropertySchema에
/// 노출하는 것"뿐이며, 실제로 이 노드가 입력을 받았을 때 <c>SequenceExecutor</c>(SQ-01)를 실제로
/// 시작시키는 런타임 배선은 이 Step 범위 밖입니다(후속 Step에서 <c>OnInputAsync</c>를 갱신할 예정 —
/// <c>PlcTagReadNode</c>가 ED-D04에서 TagId 패스스루로 시작해 PD-01e에서 실제 값 읽기로 갱신됐던 것과
/// 동일한 "뼈대 우선, 확장" 패턴).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>이 클래스도 <see cref="PlcTagRead.PlcTagReadNode"/>와 동일한 선례로 <see cref="IFlowNode"/>를
/// 직접 구현합니다(<c>LssLibNodeAdapterBase</c>류의 베이스 클래스는 실제 코드베이스에 없음).</item>
/// <item><see cref="SequenceId"/>가 <c>string</c>인 이유: 이 프로젝트(<c>nodes\*</c>)는 관례상
/// Contracts만 참조하고, 실제 시퀀스 선택 UI는 WPF 전용인 <c>NodeSharp.Editor</c>가 담당하기
/// 때문입니다(<c>PlcTagReadNode.TagId</c> XML 문서와 동일한 판단).</item>
/// </list>
/// </remarks>
public sealed class SequenceTriggerNode : IFlowNode
{
    /// <inheritdoc />
    public string Id { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Type => "sequenceTrigger";

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <summary>입력 1개 — 이 포트로 msg가 들어오면 시퀀스를 시작시키는 것이 최종 목표입니다(현재는 패스스루, 클래스 XML 문서의 범위 축소 참고).</summary>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <summary>출력 1개.</summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>
    /// 이 노드가 가리키는 <c>SequenceDefinition.Id</c>입니다. Editor 쪽 <c>NodePropertyDialog</c>가
    /// "sequenceId"(<c>PropertyFieldType.SequenceRef</c>) 필드로 편집하고, 더블클릭 시 Sequence Editor
    /// 창을 열 때(<c>FlowCanvasView.OnCardMouseLeftButtonDown</c>) 이 값을 그대로 넘겨줍니다.
    /// </summary>
    public string SequenceId { get; init; } = string.Empty;

    /// <summary>연결 초기화가 필요 없어 아무 것도 하지 않습니다.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// (SQ-03, ★ 범위 축소) 아직 <c>SequenceExecutor</c>를 실제로 시작시키지 않고, 입력 msg를
    /// 그대로 0번 출력 포트로 전달합니다 — 이 노드가 실제로 시퀀스를 구동하는 것은 후속 Step 범위입니다.
    /// </summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
        ctx.RouteAsync(Id, outputPort: 0, msg, ct);

    /// <summary>정리할 연결·구독이 없어 아무 것도 하지 않습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
}
