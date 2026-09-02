using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.PlcTagRead;

/// <summary>
/// Class명 : PLC 태그 읽기 노드
/// 역활 및 기능 : 구조 설정 트리의 태그(TagNode) 하나를 TagRef로 참조해, DeviceMapPoller가 채운
/// TagValueCache의 실제 최신 값을 msg.payload로 전달하는 노드
///
/// (ED-D04) 02번 설계문서 9번 탭 카드5의 PlcTagReadNode 예시를 좁은 범위로 구현했습니다. 카드5
/// 원본은 <c>ctx.Structure</c>(<c>IStructureService</c>, 구조 설정 데이터를 Runner가 읽는 런타임
/// 인터페이스)로 실제 PLC Raw 값을 읽고 스케일까지 적용하는 완전한 동작을 예시하지만,
/// <c>IStructureService</c>는 아직 어디에도 구현되어 있지 않습니다(Card 7이 설계만 해뒀고, ED-D03이
/// 구현을 명시적으로 보류한 상태 — 자세한 경위는 03번 Step맵 ED-D04 항목 참고). 이 Step 당시의 완료
/// 기준("구조 설정에서 태그 이름만 변경해도 캔버스 노드의 TagRef 연동이 끊기지 않는지 확인")은 실제
/// PLC 통신 여부와 무관하게 Id 안정성만 증명하면 충분해, 그때는 <see cref="TagId"/>를 그대로
/// msg.payload에 실어 전달하는 자리표시자 동작으로 한정했습니다.
/// (PD-01e, ★ 갱신) <c>ctx.GetTagValue(TagId)</c>(<c>INodeContext</c> 신규 멤버)로 실제 폴링된 값을
/// 읽어 payload로 전달하도록 바꿨습니다 — <c>IStructureService</c>는 여전히 구현되어 있지 않지만,
/// PD-01e가 만든 Runner 쪽 시뮬레이션 파이프라인(<c>SimulationDeviceBinder</c>→<c>DeviceMapPoller</c>→
/// <c>TagValueCache</c>)이 이제 <see cref="TagId"/>가 가리키는 실제(시뮬레이션) 값을 채워주므로, 이
/// 노드가 그 값을 그대로 돌려주는 것이 자연스럽습니다. 값이 아직 갱신되지 않았으면(시뮬레이션 모드가
/// 아닌 PLC, 아직 첫 폴링 전 등) <c>null</c>이 그대로 payload가 됩니다 — 이 노드는 오류로 취급하지
/// 않습니다(호출부가 Switch/Function 노드 등으로 null 여부를 직접 판단하면 됨, <c>DeviceMapPoller.GetCached</c>
/// 문서의 "아직 한 번도 갱신되지 않았으면 null" 규약과 동일).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>이 클래스는 카드5 예시의 <c>LssLibNodeAdapterBase</c>를 상속하지 않습니다 — 그 베이스
/// 클래스는 실제 코드베이스 어디에도 존재하지 않고(카드5의 스케치성 예시로 보임), 실제 코어 노드
/// (InjectNode/SwitchNode/FunctionNode/DebugNode) 전부가 <see cref="IFlowNode"/>를 직접 구현하는
/// 선례를 그대로 따랐습니다.</item>
/// <item><see cref="TagId"/>가 <c>string</c>인 이유: 이 프로젝트(<c>nodes\*</c>)는 관례상
/// Contracts만 참조하고, 실제 태그 선택 UI(<c>NodePropertyDialog</c>의 TagRef 콤보박스, 태그의
/// <c>StructureTreeNode.Id</c>를 값으로 씀)는 WPF 전용인 <c>NodeSharp.Editor</c>가 담당하기
/// 때문입니다 — 이 클래스는 이미 선택된 문자열 Id를 그대로 받아쓸 뿐, 그 Id가 무엇을 가리키는지는
/// 알지 못합니다(IStructureService 도입 이후에야 실제로 해석됩니다).</item>
/// </list>
/// </remarks>
public sealed class PlcTagReadNode : IFlowNode
{
    /// <inheritdoc />
    public string Id { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Type => "plcTagRead";

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <summary>입력 1개 — 이 포트로 msg가 들어올 때마다 <see cref="TagId"/>를 실어 그대로 전달합니다(트리거 역할).</summary>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <summary>출력 1개.</summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>
    /// (ED-D04) 구조 설정 트리에서 선택한 태그의 고유 Id입니다(Editor 쪽 <c>NodePropertyDialog</c>가
    /// TagRef 콤보박스 선택값으로 채웁니다). 태그 이름이 바뀌어도 이 Id는 그대로라 연동이 끊기지
    /// 않습니다(완료 기준).
    /// </summary>
    public string TagId { get; init; } = string.Empty;

    /// <summary>연결 초기화가 필요 없어 아무 것도 하지 않습니다.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// (PD-01e, ★ 갱신) <c>ctx.GetTagValue(TagId)</c>로 얻은 실제(시뮬레이션) 최신 값을 payload에
    /// 실어 0번 출력 포트로 전달합니다 — 위 클래스 문서의 "null이면 그대로 null" 규약 그대로입니다.
    /// </summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
    {
        var forwarded = new Msg { Payload = ctx.GetTagValue(TagId) };
        return ctx.RouteAsync(Id, outputPort: 0, forwarded, ct);
    }

    /// <summary>정리할 연결·구독이 없어 아무 것도 하지 않습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
}
