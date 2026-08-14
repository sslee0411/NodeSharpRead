using Microsoft.Extensions.Hosting;
using NodeSharp.Registry;
using NodeSharp.Runner.Core;
using NodeSharp.Runner.Health;
using NodeSharp.Runtime;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 러너 워커
/// 역활 및 기능 : Generic Host 기동 시 StartupSequencer로 설정 파일을 순서대로 읽고 flows.json이 있으면 FlowDeployer로 배포한 뒤, 5분마다 클럭 드리프트·디스크 여유 공간을 확인해 RunnerHealthState에 기록하는 백그라운드 서비스
///
/// (RN-B0) NodeSharp.Runner의 백그라운드 서비스 진입점으로 처음 만들어졌을 때는 아무 일도 하지
/// 않는 빈 뼈대였습니다. (RN-01a) <see cref="StartupSequencer"/>로 device.json→sequences.json→
/// flows.json→dashboard.json을 고정 순서로 로딩하도록 채워졌습니다. (RN-02) flows.json 단계가
/// 성공했을 때 <see cref="FlowDeployer"/>로 실제 <c>FlowEngine.DeployAsync</c>까지 호출하도록
/// 이어졌습니다 — 아직 실제 노드 타입이 없어(Phase 7~8 예정) registry는 빈 상태로 넘깁니다.
/// (RN-04a) 배포에 성공하면 그 결과를 <see cref="RunnerHealthState"/>에 기록해 /health
/// 엔드포인트가 최신 값을 돌려줄 수 있게 했습니다. (RN-05a) 배포 시도가 끝난 뒤 5분 주기로
/// <see cref="ClockDriftMonitor"/>를 호출해 결과를 계속 기록하는 반복 루프가 추가됐습니다.
/// (RN-05b-a) 같은 5분 주기 루프에서 <see cref="DiskSpaceMonitor"/>도 함께 호출해 디스크 여유
/// 공간을 기록합니다(Critical 시 RetentionSweeper 강제 실행 연동은 RN-05b-b에서 이어집니다).
/// (LK-01) 초기 배포 직후 <see cref="FlowFileWatcher"/>를 만들어 <c>flows.json.signal</c> 변경을
/// 감시합니다 — 신호가 오면 <c>FlowDeployer.RedeployAsync</c>로 같은(또는 아직 없었다면 새) 엔진에
/// 재배포하고, 성공하면 <see cref="RunnerHealthState.RecordDeploy"/>도 다시 호출해 /health가 최신
/// 배포 결과를 돌려주게 합니다.
/// </summary>
/// <example>
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// builder.Services.AddHostedService&lt;Worker&gt;();
/// builder.Services.AddSingleton&lt;RunnerHealthState&gt;();
/// var app = builder.Build();
/// await app.RunAsync();   // Ctrl+C 등으로 종료될 때까지 실행, 예외 없이 정상 종료됨
/// </code>
/// </example>
public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan ClockDriftCheckInterval = TimeSpan.FromMinutes(5);

    private readonly RunnerHealthState _healthState;
    private readonly ClockDriftMonitor _clockDriftMonitor;
    private readonly DiskSpaceMonitor? _diskSpaceMonitor;

    /// <summary>(LK-01) <see cref="StopAsync"/>/<see cref="Dispose"/>에서 감시를 정리할 수 있도록 필드로 보관합니다.</summary>
    private FlowFileWatcher? _flowFileWatcher;

    /// <summary>
    /// (RN-04a) DI로 <see cref="RunnerHealthState"/>를 주입받습니다 — 배포에 성공했을 때
    /// 이 인스턴스에 결과를 기록해야 /health 엔드포인트가 최신 값을 돌려줄 수 있습니다.
    /// (RN-05a) <paramref name="clockDriftMonitor"/>는 생략하면 실제 w32tm을 읽는 기본
    /// 인스턴스를 씁니다 — 테스트에서는 가짜 reader를 가진 인스턴스를 주입해 실제 OS 호출
    /// 없이 빠르게 검증합니다.
    /// (RN-05b-a) <paramref name="diskSpaceMonitor"/>도 같은 이유로 선택적으로 주입받습니다 —
    /// 생략하면 <see cref="ExecuteAsync"/>에서 실행 파일 폴더(<c>baseDirectory</c>) 기준으로 실제
    /// <c>DriveInfo</c>를 읽는 기본 인스턴스를 만듭니다(생성자 시점에는 baseDirectory를 아직 몰라
    /// ClockDriftMonitor처럼 생성자에서 바로 기본값을 만들 수 없음).
    /// </summary>
    public Worker(RunnerHealthState healthState, ClockDriftMonitor? clockDriftMonitor = null, DiskSpaceMonitor? diskSpaceMonitor = null)
    {
        _healthState = healthState;
        _clockDriftMonitor = clockDriftMonitor ?? new ClockDriftMonitor();
        _diskSpaceMonitor = diskSpaceMonitor;
    }

    /// <summary>
    /// (RN-01a) <see cref="StartupSequencer"/>로 실행 파일 폴더의 설정 파일 4개를 고정 순서로
    /// 읽습니다. (RN-02) flows.json 단계가 성공했으면 <see cref="FlowDeployer"/>로 실제 배포까지
    /// 시도합니다 — 아직 실제 노드 타입이 없으므로 registry는 빈 상태(<c>NodeTypeRegistry</c>만
    /// 생성)로 넘기고, 실제 노드 타입 등록(RG-02a/02b의 <c>LoadPlugins</c> 연동)은 Phase 7~8 이후
    /// 노드 구현체가 생기면 이어서 연결할 예정입니다. (RN-04a) 배포가 성공(engine이 null이 아님)하면
    /// <see cref="RunnerHealthState.RecordDeploy"/>를 호출해 /health가 참조할 값을 갱신합니다.
    /// (RN-05a) 그 뒤로는 취소될 때까지 5분마다 <see cref="ClockDriftMonitor.CheckAsync"/>를
    /// 호출해 결과를 기록합니다 — 한 번의 확인이 예외를 던져도(예: w32tm 파싱 실패) 잡아서
    /// 다음 주기에 다시 시도할 뿐 전체 루프는 멈추지 않습니다(StartupSequencer의 "단계별 격리"
    /// 원칙과 동일한 정신).
    /// (RN-05b-a) 같은 루프에서 <see cref="DiskSpaceMonitor.Check"/>도 호출해 결과를 기록합니다
    /// (같은 방식으로 예외를 격리). ★ Critical 판정 시 RetentionSweeper를 즉시 강제 실행하는
    /// 연동은 아직 없습니다 — RetentionSweeper(ED-D10, Phase 13)가 아직 만들어지지 않아 RN-05b-b로
    /// 분리해 이후 착수합니다(지금은 판정 결과를 기록만 함).
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var stages = await new StartupSequencer().RunAsync(baseDirectory, stoppingToken);

        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var deployer = new FlowDeployer();
        FlowEngine? engine = await deployer.DeployIfAvailableAsync(baseDirectory, stages, registry, stoppingToken);
        if (engine is not null)
        {
            _healthState.RecordDeploy(engine);
        }

        // (LK-01) flows.json.signal 변경을 감지해 자동 재배포 — 부팅 시점엔 flows.json이 아직 없어서
        // (또는 문법 오류라) engine이 null이었어도, 그 뒤 Editor에서 최초로 저장하면 이 콜백이 새
        // 엔진을 만들어 배포한다(FlowDeployer.RedeployAsync XML 문서 참고).
        _flowFileWatcher = new FlowFileWatcher(baseDirectory, async ct =>
        {
            engine = await deployer.RedeployAsync(engine, baseDirectory, registry, ct);
            if (engine is not null)
            {
                _healthState.RecordDeploy(engine);
            }
        });

        var diskSpaceMonitor = _diskSpaceMonitor ?? new DiskSpaceMonitor(baseDirectory);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var drift = await _clockDriftMonitor.CheckAsync(stoppingToken);
                _healthState.RecordClockDrift(drift);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // RN-05a: 한 번의 확인 실패(예: w32tm 파싱 실패)가 전체 루프를 멈추지 않도록 격리.
                // 다음 주기에 다시 시도한다.
            }

            try
            {
                // RN-05b-a: 디스크 여유 공간 확인 — Critical이어도 아직은 기록만 함(RN-05b-b 대기).
                var disk = diskSpaceMonitor.Check();
                _healthState.RecordDiskSpace(disk);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // RN-05b-a: 한 번의 확인 실패가 전체 루프를 멈추지 않도록 격리. 다음 주기에 다시 시도한다.
            }

            try
            {
                await Task.Delay(ClockDriftCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// (LK-01) <see cref="ExecuteAsync"/>가 만든 <see cref="_flowFileWatcher"/>를 정리합니다 —
    /// 해제하지 않으면 프로세스 종료 전까지 <see cref="FileSystemWatcher"/>가 계속 살아있게 됩니다
    /// (공통 규칙 ② — 구독/리소스는 반드시 해제).
    /// </summary>
    public override void Dispose()
    {
        _flowFileWatcher?.Dispose();
        base.Dispose();
    }
}
