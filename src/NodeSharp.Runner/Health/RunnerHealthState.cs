using NodeSharp.Runtime;

namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 러너 헬스 상태
/// 역활 및 기능 : /health 엔드포인트(RN-04a)가 반환할 가동 시간·배포 노드 수·마지막 배포 시각·실패 노드 목록·클럭 드리프트를 보관하는 DI 싱글턴
///
/// (RN-04a) Worker가 FlowEngine 배포에 성공할 때마다 <see cref="RecordDeploy"/>로 그 결과를
/// 기록해두고, /health 엔드포인트가 호출될 때마다 <see cref="Snapshot"/>으로 최신 값을 JSON
/// 직렬화 가능한 형태로 꺼내 씁니다. 가동 시작 시각(<c>StartedAt</c>)은 이 인스턴스가 생성되는
/// 시점(=Program.cs가 DI 컨테이너를 빌드하는 시점, 사실상 프로세스 기동 직후)을 기준으로 합니다.
/// 설계 근거: 02번 문서 7번 탭 카드11 <c>app.MapGet("/health", ...)</c> 의사코드.
/// (RN-05a) Worker가 5분마다 <see cref="RecordClockDrift"/>를 호출해 <c>ClockDriftMonitor</c>
/// 확인 결과도 함께 보관합니다.
/// (RN-05b-a) Worker가 같은 5분 주기로 <see cref="RecordDiskSpace"/>를 호출해 <c>DiskSpaceMonitor</c>
/// 확인 결과도 함께 보관합니다.
/// </summary>
/// <remarks>
/// ActiveAlarmCount(활성 알람 수)는 이 클래스에 아직 없습니다 — AlarmStateManager(ED-D07a,
/// Phase 9)가 아직 없어 값을 낼 방법이 없기 때문이며, 사용자 확인을 거쳐 RN-04b로 분리했습니다
/// (RN-01→RN-01a/RN-01b 분리와 동일한 판단). ED-D07a가 만들어지면 이 클래스에 필드·메서드를
/// 추가로 확장할 예정입니다.
/// </remarks>
/// <example>
/// <code>
/// // Program.cs — DI 싱글턴으로 등록
/// builder.Services.AddSingleton&lt;RunnerHealthState&gt;();
///
/// // Worker.cs — 배포에 성공했을 때, 그리고 클럭 드리프트를 확인할 때마다 기록
/// var engine = await deployer.DeployIfAvailableAsync(baseDir, stages, registry, ct);
/// if (engine is not null) healthState.RecordDeploy(engine);
/// var drift = await clockDriftMonitor.CheckAsync(ct);
/// healthState.RecordClockDrift(drift);
/// var disk = diskSpaceMonitor.Check();
/// healthState.RecordDiskSpace(disk);
///
/// // /health 엔드포인트 — 호출될 때마다 최신 스냅샷 반환
/// app.MapGet("/health", (RunnerHealthState health) =&gt; health.Snapshot());
/// </code>
/// </example>
public sealed class RunnerHealthState
{
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private FlowEngine? _lastDeployedEngine;
    private DateTime? _lastDeployAt;
    private ClockDriftStatus? _lastClockDrift;
    private DiskSpaceStatus? _lastDiskSpace;

    /// <summary>
    /// 배포가 성공할 때마다 호출합니다 — 이후 <see cref="Snapshot"/>이 이 <paramref name="engine"/>
    /// 기준으로 <c>DeployedNodeCount</c>/<c>FailedNodeIds</c>를 계산합니다.
    /// </summary>
    public void RecordDeploy(FlowEngine engine)
    {
        _lastDeployedEngine = engine;
        _lastDeployAt = DateTime.UtcNow;
    }

    /// <summary>
    /// (RN-05a) <c>ClockDriftMonitor.CheckAsync</c>가 새 결과를 낼 때마다 호출합니다 — 이후
    /// <see cref="Snapshot"/>의 <c>ClockDrift</c> 필드가 이 값을 그대로 반환합니다.
    /// </summary>
    public void RecordClockDrift(ClockDriftStatus status)
    {
        _lastClockDrift = status;
    }

    /// <summary>
    /// (RN-05b-a) <c>DiskSpaceMonitor.Check</c>가 새 결과를 낼 때마다 호출합니다 — 이후
    /// <see cref="Snapshot"/>의 <c>DiskSpace</c> 필드가 이 값을 그대로 반환합니다.
    /// </summary>
    public void RecordDiskSpace(DiskSpaceStatus status)
    {
        _lastDiskSpace = status;
    }

    /// <summary>/health 엔드포인트가 그대로 JSON 직렬화해 반환할 현재 상태 스냅샷을 새로 계산해 반환합니다.</summary>
    public RunnerHealthSnapshot Snapshot() => new(
        Status: "Healthy",
        UptimeSeconds: (DateTime.UtcNow - _startedAt).TotalSeconds,
        DeployedNodeCount: _lastDeployedEngine?.Nodes.Count ?? 0,
        LastDeployAt: _lastDeployAt,
        FailedNodeIds: _lastDeployedEngine?.FailedNodeIds ?? Array.Empty<string>(),
        ClockDrift: _lastClockDrift,
        DiskSpace: _lastDiskSpace);
}
