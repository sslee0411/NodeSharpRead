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
        CancellationToken ct)
    {
        var flowsStage = startupStages.FirstOrDefault(s => s.FileName == "flows.json");
        if (flowsStage is not { Succeeded: true })
        {
            return null;
        }

        var flowsPath = Path.Combine(baseDirectory, "flows.json");
        var json = await File.ReadAllTextAsync(flowsPath, ct);
        var flows = JsonSerializer.Deserialize<List<FlowDefinition>>(json);
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
        var merged = new FlowDefinition(Id: "__merged__", Name: "전체 배포", Nodes: mergedNodes, Wires: mergedWires);

        var engine = new FlowEngine(registry);
        new NodeStatusConsoleLogger().Subscribe(engine.EventBus);
        await engine.DeployAsync(merged, DeployMode.Full, ct);
        return engine;
    }
}
