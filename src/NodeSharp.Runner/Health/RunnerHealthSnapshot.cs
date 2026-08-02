namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 러너 헬스 스냅샷
/// 역활 및 기능 : /health 엔드포인트가 그대로 JSON 직렬화해 반환하는 상태 값 묶음(RN-04a)
///
/// (RN-04a) <c>RunnerHealthState.Snapshot()</c>이 매 호출마다 최신 값으로 새로 만들어 반환하는
/// 불변 레코드입니다. 필드 구성은 02번 문서 7번 탭 카드11 <c>app.MapGet("/health", ...)</c>
/// 의사코드를 따르되, <c>ActiveAlarmCount</c>(활성 알람 수)는 <c>AlarmStateManager</c>(ED-D07a,
/// Phase 9)가 아직 없어 이 Step 범위에서 제외했습니다(사용자 확인 거쳐 RN-04b로 분리).
/// (RN-05a) <see cref="ClockDrift"/> 필드를 추가해 카드12의 클럭 드리프트 값도 함께 노출합니다.
/// </summary>
/// <param name="Status">현재 상태 배지 문자열. 이 Step에서는 항상 "Healthy"를 반환하고, Degraded/Unhealthy 판정 로직(디스크 등)은 해당 항목이 구현되는 이후 Step에서 추가됩니다.</param>
/// <param name="UptimeSeconds">프로세스가 기동한 이후 지난 시간(초). <see cref="RunnerHealthState"/>가 생성된 시각을 기준으로 계산됩니다.</param>
/// <param name="DeployedNodeCount">가장 최근 배포에서 <c>FlowEngine</c>이 실제로 생성한 노드 인스턴스 수. 아직 한 번도 배포되지 않았으면 0입니다.</param>
/// <param name="LastDeployAt">가장 최근 배포가 성공한 시각(UTC). 아직 한 번도 배포되지 않았으면 null입니다.</param>
/// <param name="FailedNodeIds">가장 최근 배포에서 OnStartAsync가 실패해 격리된 노드 Id 목록(RT-02b). 배포된 적이 없으면 빈 목록입니다.</param>
/// <param name="ClockDrift">가장 최근 <c>ClockDriftMonitor.CheckAsync</c> 확인 결과(RN-05a). 아직 한 번도 확인하지 않았으면 null입니다.</param>
public sealed record RunnerHealthSnapshot(
    string Status,
    double UptimeSeconds,
    int DeployedNodeCount,
    DateTime? LastDeployAt,
    IReadOnlyList<string> FailedNodeIds,
    ClockDriftStatus? ClockDrift = null);
