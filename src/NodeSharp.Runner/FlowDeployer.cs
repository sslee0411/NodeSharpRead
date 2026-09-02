using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 플로우 배포기
/// 역활 및 기능 : StartupSequencer가 확인만 하고 버린 flows.json을 다시 읽어 FlowEngine에 실제로 배포하고 콘솔 상태 로그를 연결하는 클래스
///
/// (RN-02) <c>StartupSequencer</c>(RN-01a)는 flows.json이 문제없이 파싱되는지"만" 확인하고 그
/// 내용은 버립니다(RN-01a 완료 기준이 "로딩 순서 + 파일별 장애 격리"로 좁혀졌기 때문이며, 이미
/// 확정된 코드라 시그니처를 바꾸지 않습니다). 이 클래스는 flows.json 단계가 성공했을 때만 파일을
/// 한 번 더 읽어 실제로 <c>FlowEngine.DeployAsync</c>를 호출하고, <see cref="NodeStatusConsoleLogger"/>를
/// <c>FlowEngine.EventBus</c>에 연결해 배포된 노드의 상태를 콘솔에서 볼 수 있게 합니다.
/// </summary>
/// <remarks>
/// 아직 Inject/Function/Switch/Debug 같은 실제 노드 타입이 없습니다(Phase 7~8에서 구현 예정).
/// 그래서 실제 운영 시점의 <c>registry</c>에는 아무 타입도 등록돼 있지 않을 수 있고, 그 경우
/// flows.json에 노드가 있어도 <see cref="INodeRegistry.CreateInstance"/>가 예외를 던지는 대신
/// <c>FlowEngine.DeployAsync</c>가 이를 잡아 <c>MissingNode</c>로 표시할 뿐 전체가 죽지 않습니다
/// (RT-02b). 이 Step의 완료 기준은 "더미 노드 1개로 배포 메커니즘 자체가 동작하는가"이며, 실제
/// 검증은 테스트에서 더미 노드 타입 하나를 registry에 등록해 직접 확인합니다(Inject/Function/
/// Switch/Debug 등 진짜 노드 동작 확인은 Phase 8 LK-02).
/// <para>
/// (★ EC-05 확장, 사용자 요청, v2.51) flows.json이 이제 <see cref="FlowDefinition"/> 목록(Flow 탭
/// 개수만큼)이라 <see cref="FlowEngine.DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>
/// (단일 <see cref="FlowDefinition"/>만 받는 이미 확정된 <c>RT-01b</c>~<c>RT-09b</c> API, <c>_currentFlow</c>
/// 기준으로 노드 diff를 계산하므로 여러 번 나눠 호출하면 이전 호출의 노드가 "사라진 것"으로 오인돼
/// 잘못 종료됨)를 그대로 재사용하기 위해, <see cref="Disabled"/>가 아닌 탭들의 Nodes/Wires를 이
/// 클래스에서 <b>하나로 병합</b>한 뒤 <c>DeployAsync</c>를 <b>한 번만</b> 호출합니다 — 실제 Node-RED가
/// 모든 활성 탭을 항상 동시에 배포하는 동작과 일치하며, <c>FlowEngine</c>/<c>RT-0x</c> 쪽은 전혀
/// 수정하지 않습니다(이미 확정된 코드 비변경 원칙, 위 문서 "RN-01a는 시그니처를 바꾸지 않습니다"와
/// 동일한 정신). 병합 단위의 <see cref="FlowDefinition.Id"/>/<see cref="FlowDefinition.Name"/>은
/// 표시용일 뿐 <c>FlowEngine</c> 동작에 영향을 주지 않아 고정값(<c>"__merged__"</c>)을 씁니다.
/// </para>
/// <para>
/// (LK-01) <see cref="RedeployAsync"/> 신규 — <c>Core\FlowFileWatcher</c>가 <c>flows.json.signal</c>
/// 변경을 감지했을 때 호출됩니다. <see cref="DeployIfAvailableAsync"/>와 달리 <c>StartupStageResult</c>
/// 게이트가 없습니다(부팅 시퀀스 밖에서 프로세스 수명 내내 반복 호출되므로 부팅 시점 1회성 검사와는
/// 무관) — 대신 flows.json이 없거나 JSON 파싱에 실패하거나(저장 도중 신호가 먼저 도착하는 극단적
/// 경합 등) 활성 탭이 하나도 없으면 아무 것도 하지 않고 기존 엔진을 그대로 돌려줍니다(있던 걸 잘못
/// 지우지 않음). 두 메서드가 "flows.json을 읽어 활성 탭만 병합"하는 로직을 공유하도록
/// <see cref="MergeActiveFlows"/>로 뽑아냈습니다(중복 제거, 동작은 기존과 완전히 동일).
/// <see cref="RedeployAsync"/>에 이미 떠 있는 <see cref="FlowEngine"/>을 넘기면(재배포) 그 위에 그대로
/// <c>DeployAsync(..., DeployMode.Full, ...)</c>를 호출합니다 — <c>FlowEngine.DeployAsync</c>가
/// Full 모드에서도 "새 <c>FlowDefinition</c>에 없는 기존 노드는 <c>OnCloseAsync</c>로 먼저 정리한다"는
/// 것을 <c>RT-03</c> 구현에서 이미 확인했으므로(코드 재확인, 착수 전 조사), 매번 <c>new FlowEngine(...)</c>을
/// 새로 만들지 않고 <b>같은 엔진 인스턴스를 계속 재사용</b>합니다 — 새 엔진을 매번 만들면 이전
/// 엔진이 갖고 있던 노드(예: Inject의 Interval 타이머)가 <c>OnCloseAsync</c> 없이 그대로 버려져
/// 타이머가 계속 백그라운드에서 도는 누수가 생기기 때문입니다. <paramref name="existingEngine"/>이
/// <c>null</c>이면(부팅 시점엔 flows.json이 아직 없었지만 Editor에서 그 뒤 처음 저장한 경우) 새
/// 엔진을 만들어 <see cref="NodeStatusConsoleLogger"/>도 함께 구독시킵니다(<see cref="DeployIfAvailableAsync"/>가
/// 하던 것과 동일한 조립 — <see cref="CreateEngineWithLogger"/>로 공유).
/// </para>
/// <para>
/// (LK-02a) <see cref="DeployIfAvailableAsync"/>·<see cref="RedeployAsync"/> 둘 다 새 선택적 매개변수
/// <c>attachMonitor</c>(<c>Func&lt;IEventBus, IDisposable&gt;?</c>, 기본값 <c>null</c>)를 받습니다 — 기존
/// 호출부(테스트 포함)는 아무것도 바꾸지 않아도 그대로 동작합니다(트레일링 선택적 매개변수, <c>EC-05
/// 확장</c>의 <c>PropertyField.VisibleWhenKey</c> 추가와 동일한 하위 호환 방식). <c>Worker</c>가 이
/// 자리에 <c>eventBus =&gt; statusBroadcaster.Subscribe(eventBus)</c>를 넘기면, 진짜 새 <see cref="FlowEngine"/>이
/// 만들어질 때(<see cref="CreateEngineWithLogger"/> 문서 참고)만 <c>StatusBroadcaster</c>가 그 엔진의
/// <c>EventBus</c>를 구독해 SignalR로 중계합니다. 이 파일은 <c>IEventBus</c>/<c>IDisposable</c>(둘 다
/// 이미 이 프로젝트가 참조하는 Contracts/System 타입)만 알면 되므로, Runner의 실제 SignalR 의존
/// (<c>Microsoft.AspNetCore.SignalR</c>)은 <c>Core\StatusBroadcaster.cs</c>에만 격리됩니다 — 이 클래스와
/// <c>NodeSharp.Tests</c>의 <c>FlowDeployerTests</c>는 SignalR을 몰라도 계속 컴파일·테스트됩니다.
/// </para>
/// <para>
/// (PD-01e) <see cref="DeployIfAvailableAsync"/>·<see cref="RedeployAsync"/>·<see cref="CreateEngineWithLogger"/>
/// 셋 다 새 트레일링 선택적 매개변수 <c>tagValueCache</c>(<c>TagValueCache?</c>, 기본값 <c>null</c>)를
/// 받습니다 — <c>attachMonitor</c>와 동일한 하위 호환 방식으로 <c>Worker</c>가 <c>DeviceMapPoller</c>들과
/// 공유하는 <see cref="TagValueCache"/> 인스턴스를 넘기면, <c>CreateEngineWithLogger</c>가 그것을
/// <c>new FlowEngine(..., tagValueCache: tagValueCache)</c>로 전달해 그 엔진의 모든 <c>NodeContext</c>가
/// <c>ctx.GetTagValue(tagId)</c>로 같은 캐시를 조회할 수 있게 합니다(<c>PlcTagReadNode</c> 참고).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var stages = await new StartupSequencer().RunAsync(baseDir, ct);
/// var engine = await new FlowDeployer().DeployIfAvailableAsync(baseDir, stages, registry, ct);
/// // engine이 null이 아니면 flows.json의 활성 탭(Disabled==false)이 모두 병합 배포된 것이고,
/// // 배포된 노드가 ctx.SetStatus(...)를 호출할 때마다 콘솔에 한 줄씩 로그가 찍힌다
/// </code>
/// </example>
public sealed class FlowDeployer
{
    /// <summary>
    /// <paramref name="startupStages"/>에서 "flows.json" 단계가 성공했을 때만 파일을 다시 읽어,
    /// 비활성화(<see cref="FlowDefinition.Disabled"/>)되지 않은 모든 Flow 탭의 노드·와이어를 하나로
    /// 병합한 뒤 <paramref name="registry"/>로 만든 <see cref="FlowEngine"/>에 배포합니다. 단계가
    /// 실패했거나 역직렬화 결과가 없거나(목록이 비었거나) 활성 탭이 하나도 없으면 아무 것도 하지
    /// 않고 null을 반환합니다.
    /// </summary>
    public async Task<FlowEngine?> DeployIfAvailableAsync(
        string baseDirectory,
        IReadOnlyList<StartupStageResult> startupStages,
        INodeRegistry registry,
        CancellationToken ct,
        Func<IEventBus, IDisposable>? attachMonitor = null,
        TagValueCache? tagValueCache = null)
    {
        var flowsStage = startupStages.FirstOrDefault(s => s.FileName == "flows.json");
        if (flowsStage is not { Succeeded: true })
        {
            return null;
        }

        var flowsPath = Path.Combine(baseDirectory, "flows.json");
        var json = await File.ReadAllTextAsync(flowsPath, ct);
        var flows = JsonSerializer.Deserialize<List<FlowDefinition>>(json);
        var merged = MergeActiveFlows(flows);
        if (merged is null)
        {
            return null;
        }

        var engine = CreateEngineWithLogger(registry, attachMonitor, tagValueCache);
        await engine.DeployAsync(merged, DeployMode.Full, ct);
        return engine;
    }

    /// <summary>
    /// (LK-01) <c>flows.json.signal</c> 변경 감지 시 <c>Core\FlowFileWatcher</c>가 호출하는 재배포
    /// 진입점입니다. <paramref name="existingEngine"/>이 있으면(이미 배포된 적 있음) 그 인스턴스에
    /// 그대로 재배포하고(위 클래스 remarks의 LK-01 항목 참고 — 노드 누수 방지), 없으면 새로 만들어
    /// 배포합니다. flows.json이 없거나 JSON 파싱에 실패하거나 활성 탭이 하나도 없으면
    /// <paramref name="existingEngine"/>을 그대로 반환합니다(원래 <c>null</c>이었으면 계속 <c>null</c>).
    /// </summary>
    public async Task<FlowEngine?> RedeployAsync(
        FlowEngine? existingEngine,
        string baseDirectory,
        INodeRegistry registry,
        CancellationToken ct,
        Func<IEventBus, IDisposable>? attachMonitor = null,
        TagValueCache? tagValueCache = null)
    {
        var flowsPath = Path.Combine(baseDirectory, "flows.json");
        if (!File.Exists(flowsPath))
        {
            return existingEngine;
        }

        List<FlowDefinition>? flows;
        try
        {
            var json = await File.ReadAllTextAsync(flowsPath, ct);
            flows = JsonSerializer.Deserialize<List<FlowDefinition>>(json);
        }
        catch (JsonException)
        {
            // flows.json 저장(.tmp → File.Replace)과 .signal 발행 사이는 원자적 저장이 이미 끝난
            // 뒤라 정상적으로는 발생하지 않지만(EC-04 JsonWriteService 순서 참고), 혹시 모를 손상된
            // 파일 상태에서도 재배포 시도 전체가 죽지 않도록 방어 — 다음 신호가 오면 다시 시도된다.
            return existingEngine;
        }

        var merged = MergeActiveFlows(flows);
        if (merged is null)
        {
            return existingEngine;
        }

        var engine = existingEngine ?? CreateEngineWithLogger(registry, attachMonitor, tagValueCache);
        await engine.DeployAsync(merged, DeployMode.Full, ct);
        return engine;
    }

    /// <summary>
    /// (LK-01) <see cref="DeployIfAvailableAsync"/>·<see cref="RedeployAsync"/>가 공유하는 조립 로직 —
    /// 새 <see cref="FlowEngine"/>을 만들고 <see cref="NodeStatusConsoleLogger"/>를 그 <c>EventBus</c>에
    /// 구독시켜 반환합니다. (LK-02a) <paramref name="attachMonitor"/>가 있으면(Runner의 <c>Worker</c>가
    /// <c>eventBus =&gt; statusBroadcaster.Subscribe(eventBus)</c>로 넘김) 같은 <c>EventBus</c>에 그것도
    /// 호출합니다 — 이 메서드는 "진짜 새 <see cref="FlowEngine"/>을 만드는" 유일한 지점이라(호출부인
    /// <see cref="RedeployAsync"/>는 <c>existingEngine</c>이 있으면 이 메서드 자체를 부르지 않음), 여기서만
    /// 연결하면 <c>StatusBroadcaster</c>가 같은 엔진에 중복 구독되는 일이 없습니다(위 <c>StatusBroadcaster</c>
    /// 클래스 remarks의 "구독 시점" 항목 참고). <c>IEventBus</c>/<c>IDisposable</c>만 참조하므로 이 파일과
    /// <c>NodeSharp.Tests</c>는 SignalR 타입을 몰라도 됩니다(순수 콜백 위임 — Runner의 <c>StatusBroadcaster</c>가
    /// 실제 SignalR 의존을 전담).
    /// </summary>
    private static FlowEngine CreateEngineWithLogger(INodeRegistry registry, Func<IEventBus, IDisposable>? attachMonitor = null, TagValueCache? tagValueCache = null)
    {
        var engine = new FlowEngine(registry, tagValueCache: tagValueCache);
        new NodeStatusConsoleLogger().Subscribe(engine.EventBus);
        attachMonitor?.Invoke(engine.EventBus);
        return engine;
    }

    /// <summary>
    /// (★ EC-05 확장 로직을 LK-01에서 재사용 가능하도록 추출) <paramref name="flows"/>에서
    /// <see cref="FlowDefinition.Disabled"/>가 아닌 탭들의 Nodes/Wires를 하나로 병합합니다(위 클래스
    /// remarks 참고). <paramref name="flows"/>가 <c>null</c>이거나 비어 있거나 활성 탭이 하나도 없으면
    /// <c>null</c>을 반환합니다.
    /// </summary>
    private static FlowDefinition? MergeActiveFlows(List<FlowDefinition>? flows)
    {
        if (flows is null || flows.Count == 0)
        {
            return null;
        }

        // (★ EC-05 확장) Disabled 탭은 배포 대상에서 제외 — FlowDefinition.Disabled의 원래 의미
        // ("탭 전체가 비활성화되어 있으면 배포 시 이 탭에 속한 노드는 하나도 생성되지 않는다") 그대로.
        var activeFlows = flows.Where(f => !f.Disabled).ToList();
        if (activeFlows.Count == 0)
        {
            return null;
        }

        // 활성 탭 전체의 Nodes/Wires를 하나로 병합 — FlowEngine.DeployAsync는 단일 FlowDefinition
        // 기준으로 diff를 계산하므로(위 remarks 참고), 여러 번 나눠 호출하지 않고 한 번만 호출한다.
        var mergedNodes = activeFlows.SelectMany(f => f.Nodes).ToList();
        var mergedWires = activeFlows.SelectMany(f => f.Wires).ToList();
        return new FlowDefinition(Id: "__merged__", Name: "전체 배포", Nodes: mergedNodes, Wires: mergedWires);
    }
}
