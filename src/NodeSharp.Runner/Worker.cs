using Microsoft.Extensions.Hosting;
using NodeSharp.Registry;
using NodeSharp.Runner.Health;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 러너 워커
/// 역활 및 기능 : Generic Host 기동 시 StartupSequencer로 설정 파일을 순서대로 읽고 flows.json이 있으면 FlowDeployer로 배포한 뒤 RunnerHealthState에 기록하는 백그라운드 서비스
///
/// (RN-B0) NodeSharp.Runner의 백그라운드 서비스 진입점으로 처음 만들어졌을 때는 아무 일도 하지
/// 않는 빈 뼈대였습니다. (RN-01a) <see cref="StartupSequencer"/>로 device.json→sequences.json→
/// flows.json→dashboard.json을 고정 순서로 로딩하도록 채워졌습니다. (RN-02) flows.json 단계가
/// 성공했을 때 <see cref="FlowDeployer"/>로 실제 <c>FlowEngine.DeployAsync</c>까지 호출하도록
/// 이어졌습니다 — 아직 실제 노드 타입이 없어(Phase 7~8 예정) registry는 빈 상태로 넘깁니다.
/// (RN-04a) 배포에 성공하면 그 결과를 <see cref="RunnerHealthState"/>에 기록해 /health
/// 엔드포인트가 최신 값을 돌려줄 수 있게 했습니다.
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
    private readonly RunnerHealthState _healthState;

    /// <summary>
    /// (RN-04a) DI로 <see cref="RunnerHealthState"/>를 주입받습니다 — 배포에 성공했을 때
    /// 이 인스턴스에 결과를 기록해야 /health 엔드포인트가 최신 값을 돌려줄 수 있습니다.
    /// </summary>
    public Worker(RunnerHealthState healthState)
    {
        _healthState = healthState;
    }

    /// <summary>
    /// (RN-01a) <see cref="StartupSequencer"/>로 실행 파일 폴더의 설정 파일 4개를 고정 순서로
    /// 읽습니다. (RN-02) flows.json 단계가 성공했으면 <see cref="FlowDeployer"/>로 실제 배포까지
    /// 시도합니다 — 아직 실제 노드 타입이 없으므로 registry는 빈 상태(<c>NodeTypeRegistry</c>만
    /// 생성)로 넘기고, 실제 노드 타입 등록(RG-02a/02b의 <c>LoadPlugins</c> 연동)은 Phase 7~8 이후
    /// 노드 구현체가 생기면 이어서 연결할 예정입니다. (RN-04a) 배포가 성공(engine이 null이 아님)하면
    /// <see cref="RunnerHealthState.RecordDeploy"/>를 호출해 /health가 참조할 값을 갱신합니다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var stages = await new StartupSequencer().RunAsync(baseDirectory, stoppingToken);

        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = await new FlowDeployer().DeployIfAvailableAsync(baseDirectory, stages, registry, stoppingToken);
        if (engine is not null)
        {
            _healthState.RecordDeploy(engine);
        }
    }
}
