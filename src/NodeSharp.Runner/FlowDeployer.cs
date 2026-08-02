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
/// </remarks>
/// <example>
/// <code>
/// var stages = await new StartupSequencer().RunAsync(baseDir, ct);
/// var engine = await new FlowDeployer().DeployIfAvailableAsync(baseDir, stages, registry, ct);
/// // engine이 null이 아니면 flows.json이 배포된 것이고, 배포된 노드가 ctx.SetStatus(...)를
/// // 호출할 때마다 콘솔에 한 줄씩 로그가 찍힌다
/// </code>
/// </example>
public sealed class FlowDeployer
{
    /// <summary>
    /// <paramref name="startupStages"/>에서 "flows.json" 단계가 성공했을 때만 파일을 다시 읽어
    /// <paramref name="registry"/>로 만든 <see cref="FlowEngine"/>에 배포합니다. 단계가 실패했거나
    /// 역직렬화 결과가 null이면 아무 것도 하지 않고 null을 반환합니다.
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
        var flow = JsonSerializer.Deserialize<FlowDefinition>(json);
        if (flow is null)
        {
            return null;
        }

        var engine = new FlowEngine(registry);
        new NodeStatusConsoleLogger().Subscribe(engine.EventBus);
        await engine.DeployAsync(flow, DeployMode.Full, ct);
        return engine;
    }
}
