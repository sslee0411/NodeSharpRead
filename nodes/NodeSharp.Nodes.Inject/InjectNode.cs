using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Util.Messaging;

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
/// <item><b>(NR-03a) Trigger 종류는 처음엔 Manual 하나만</b>: 카드7 원본 스케치는 <c>InjectTrigger</c>
/// (Manual/Interval/Cron/OnDeploy) 선택을 전제하지만, NR-03a는 "수동 트리거"만 다루는 Step이라 이
/// 클래스도 처음엔 Manual 동작(호출될 때마다 1회 발행)만 구현했습니다. Interval은 <b>NR-03b</b>가
/// 아래 항목대로 추가했고, Cron/OnDeploy는 각각 NR-03c/NR-03d에서 마저 확장될 예정입니다.</item>
/// <item><b>(NR-03b) Interval 트리거 추가</b>: <see cref="TriggerMode"/>가 <c>"interval"</c>이고
/// <see cref="IntervalSeconds"/>가 0보다 크면, <see cref="OnStartAsync"/>가 <see cref="Scheduler"/>
/// (<see cref="IScheduler"/>)의 <c>SchedulePeriodic</c>으로 자기 자신의 <see cref="Id"/>를 ownerId 삼아
/// 주기 발행을 등록하고, <see cref="OnCloseAsync"/>가 반드시 <c>Unschedule(Id)</c>로 해제합니다(공통
/// 규칙 ②·③ — 해제하지 않으면 재배포마다 예약이 중복 등록됨). 실제 Node-RED의 Inject 노드(20-inject.js)도
/// 자기 자신의 <c>setInterval</c>/<c>clearInterval</c>을 직접 소유하고 <c>close()</c>에서 스스로 정리하는
/// 동일한 구조입니다(별도 공유 스케줄러 서비스 없음) — NR-03b 착수 전 AskUserQuestion에서 이 선례를
/// 근거로 "InjectNode가 스케줄러를 직접 소유"를 확정했습니다. <see cref="Scheduler"/>의 기본 구현체
/// (<c>AsyncSchedulerAdapter</c>)는 원래 <c>NodeSharp.Runtime</c> 소속이었으나, 이 프로젝트가
/// "Contracts만 참조" 원칙이라 직접 참조할 수 없어 <c>NodeSharp.Util</c>로 이동했습니다(NR-03b,
/// NodeSharp.Nodes.Inject.csproj 항목 참고).</item>
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

    /// <summary>
    /// (NR-03b) Trigger 모드 — <c>"manual"</c>(기본값, NR-03a) 또는 <c>"interval"</c>(NR-03b). Factory
    /// (<see cref="InjectNodeType"/>)가 <see cref="Contracts.Models.NodeConfig.Properties"/>의 "trigger"
    /// 값을 읽어 채웁니다.
    /// </summary>
    public string TriggerMode { get; init; } = "manual";

    /// <summary>
    /// (NR-03b) <see cref="TriggerMode"/>가 <c>"interval"</c>일 때 반복 간격(초)입니다. 0 이하이면
    /// <see cref="OnStartAsync"/>가 자동 발행을 시작하지 않습니다(수동 트리거만 남음).
    /// </summary>
    public double IntervalSeconds { get; init; }

    /// <summary>
    /// (NR-03b) <see cref="TriggerMode"/>가 <c>"interval"</c>일 때 매 간격마다 자동으로 발행할
    /// <see cref="Msg.Payload"/> 값입니다. Manual 모드의 <see cref="TriggerAsync"/>는 외부에서 매번
    /// payload를 직접 받으므로 이 값을 쓰지 않습니다.
    /// </summary>
    public object? DefaultPayload { get; init; }

    /// <summary>
    /// (NR-03b) Interval 트리거에 사용할 <see cref="IScheduler"/>입니다. 지정하지 않으면
    /// <see cref="OnStartAsync"/>가 기본값으로 앱 전체가 공유하는 <c>AsyncSchedulerAdapter</c>
    /// (<c>AsyncScheduler.Instance</c>를 감싼 인스턴스)를 직접 생성합니다. 테스트에서는
    /// <c>AsyncSchedulerAdapterTests</c>와 동일한 원칙으로 독립된 <c>AsyncScheduler</c> 인스턴스를 감싼
    /// 어댑터를 주입해 예약 목록이 다른 테스트와 섞이지 않게 합니다.
    /// </summary>
    public IScheduler? Scheduler { get; set; }

    /// <summary>
    /// (NR-03b) <see cref="OnStartAsync"/>에서 실제로 사용한 스케줄러 인스턴스 — <see cref="OnCloseAsync"/>
    /// 가 <c>Unschedule</c>을 호출할 대상을 기억해두기 위한 사설 필드입니다(Interval 모드가 아니면
    /// <c>null</c>로 남습니다).
    /// </summary>
    private IScheduler? _activeScheduler;

    /// <summary>
    /// 입력 포트가 없어 초기화할 연결·구독은 없습니다. (NR-03b) <see cref="TriggerMode"/>가
    /// <c>"interval"</c>이고 <see cref="IntervalSeconds"/>가 0보다 크면, <see cref="Scheduler"/>에
    /// 이 노드 자신의 <see cref="Id"/>를 ownerId로 삼아 주기 발행을 등록합니다(<see cref="IScheduler"/>
    /// XML 문서 예제와 동일한 패턴). 콜백은 <see cref="TriggerAsync"/>를 그대로 재사용합니다 — 매뉴얼
    /// 트리거와 인터벌 트리거가 같은 진입점을 공유합니다(위 클래스 remarks 참고).
    /// </summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
    {
        if (TriggerMode == "interval" && IntervalSeconds > 0)
        {
            _activeScheduler = Scheduler ?? new AsyncSchedulerAdapter();
            _activeScheduler.SchedulePeriodic(Id, TimeSpan.FromSeconds(IntervalSeconds),
                () => TriggerAsync(DefaultPayload, ctx, CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 입력 포트가 0개라 <c>FlowEngine</c>(NodeSharp.Runtime)이 이 메서드를 호출할 방법이 없습니다 —
    /// 계약을 만족시키기 위한 자리표시자로, 실제로 호출되면 즉시 완료됩니다.
    /// </summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// (NR-03b) <see cref="OnStartAsync"/>가 Interval 예약을 등록했다면 반드시 <c>Unschedule(Id)</c>로
    /// 해제합니다(공통 규칙 ②·③ — 해제하지 않으면 재배포마다 같은 예약이 중복 등록됨). Manual 모드라
    /// 예약이 없었다면(<c>_activeScheduler</c>가 <c>null</c>) 아무 일도 하지 않습니다.
    /// </summary>
    public Task OnCloseAsync(INodeContext ctx)
    {
        _activeScheduler?.Unschedule(Id);
        _activeScheduler = null;
        return Task.CompletedTask;
    }

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
