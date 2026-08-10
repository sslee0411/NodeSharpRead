using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Inject;

/// <summary>
/// Class명 : Inject 노드
/// 역활 및 기능 : 캔버스에서 트리거될 때마다 새 Msg 하나를 만들어 0번 출력 포트로 발행하는 소스 노드
///
/// Node-RED의 Inject 노드에 대응하는 소스(source) 노드입니다 — 입력 포트가 0개라 다른 노드처럼
/// <see cref="IFlowNode.OnInputAsync"/>로 동작이 시작되지 않고, 항상 외부(지금은 xUnit 테스트, 향후
/// LK-02가 붙으면 Editor→Runner 채널)가 <see cref="TriggerAsync"/>를 직접 호출해야 메시지가
/// 발행됩니다(02번 문서 9번 탭 카드7 "Manual은 Editor의 '노드 클릭' 이벤트가 별도 채널로 FireAsync를
/// 직접 호출"이 의도한 그대로 — <see cref="IFlowNode"/> 계약 밖의 공개 메서드로 노출).
/// 설계 근거: 02번 문서 9번 탭 카드7(InjectNode 설계 스케치), 03번 개발 Step맵 NR-03a.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Trigger 종류는 Manual 하나만</b>: 카드7 원본 스케치는 <c>InjectTrigger</c>(Manual/Interval/
/// Cron/OnDeploy) 선택을 전제하지만, NR-03a는 "수동 트리거"만 다루는 Step이라 이 클래스도 Manual
/// 동작(호출될 때마다 1회 발행)만 구현합니다. Interval/Cron/OnDeploy는 각각 별도 Step(NR-03b 등)에서
/// 이 클래스에 스케줄링 로직을 추가하는 형태로 확장될 예정입니다.</item>
/// <item><b>카드7 원본 코드와의 차이</b>: 카드7 스니펫은 <c>NodeContext ctx</c>(구체 클래스)와
/// <c>ctx.Engine.RouteAsync(...)</c>를 사용하지만, 실제로 확정된 계약은 <see cref="INodeContext"/>
/// (인터페이스)와 <see cref="INodeContext.RouteAsync"/>(엔진을 거치지 않고 컨텍스트가 직접 노출)입니다
/// — 이 클래스는 카드7의 "설계 의도"(Manual 트리거 → 1회 발행)만 따르고, 실제 코드는 현재 Contracts
/// 계약(<see cref="IFlowNode"/>/<see cref="INodeContext"/>, CT-04a 이후 확정)을 그대로 사용합니다.</item>
/// </list>
/// </remarks>
public sealed class InjectNode : IFlowNode
{
    /// <inheritdoc />
    public string Id { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Type => "inject";

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <summary>Inject는 스스로 메시지를 만들어내는 소스 노드라 입력 포트가 없습니다.</summary>
    public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();

    /// <summary>발행한 메시지가 나가는 출력 포트 1개입니다.</summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>입력 포트가 없어 초기화할 연결·구독이 없습니다.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// 입력 포트가 0개라 <c>FlowEngine</c>(NodeSharp.Runtime)이 이 메서드를 호출할 방법이 없습니다 —
    /// 계약을 만족시키기 위한 자리표시자로, 실제로 호출되면 즉시 완료됩니다.
    /// </summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>구독·연결이 없어 정리할 것도 없습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;

    /// <summary>
    /// 새 <see cref="Msg"/>를 만들어(<see cref="Msg.Payload"/>에 <paramref name="payload"/> 그대로 대입)
    /// 0번 출력 포트로 정확히 1회 전달합니다 — 캔버스의 "노드 클릭"(향후 LK-02가 붙으면 Editor→Runner
    /// 채널을 거쳐 이 메서드를 호출) 또는 지금은 xUnit 테스트가 직접 호출하는 진입점입니다.
    /// <see cref="IFlowNode"/> 계약에는 없는 이 클래스 고유의 공개 메서드입니다(위 클래스 remarks
    /// 참고 — Inject는 입력 포트가 없어 <see cref="OnInputAsync"/>로는 트리거될 수 없기 때문).
    /// </summary>
    public Task TriggerAsync(object? payload, INodeContext ctx, CancellationToken ct)
    {
        var msg = new Msg { Payload = payload };
        return ctx.RouteAsync(Id, outputPort: 0, msg, ct);
    }
}
