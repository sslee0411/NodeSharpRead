using Microsoft.Extensions.Hosting;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 러너 워커
/// 역활 및 기능 : NodeSharp.Runner가 Generic Host 위에서 예외 없이 기동·종료되는지 확인하는 최소 뼈대(백그라운드 서비스)
///
/// (RN-B0) NodeSharp.Runner의 백그라운드 서비스 진입점입니다. 아직 device.json/sequences.json/
/// flows.json 로딩이나 <c>FlowEngine.DeployAsync</c> 호출은 하지 않는 빈 뼈대입니다 — 그 실제 기동
/// 시퀀스(02번 문서 3번 탭 카드8 <c>Program.cs StartupAsync</c> 의사코드: 구조 트리 →
/// Sequence 정의 → Flow 정의 순으로 로드한 뒤 DeployAsync 호출, 마지막으로 Dashboard 로드)는
/// <c>RN-01</c>에서 이 클래스의 <see cref="ExecuteAsync"/> 안에 그대로 채워질 예정입니다.
/// </summary>
/// <example>
/// <code>
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Services.AddHostedService&lt;Worker&gt;();
/// using var host = builder.Build();
/// await host.RunAsync();   // Ctrl+C 등으로 종료될 때까지 실행, 예외 없이 정상 종료됨
/// </code>
/// </example>
public sealed class Worker : BackgroundService
{
    /// <summary>
    /// (RN-B0) 아직 아무 일도 하지 않습니다 — 이 메서드가 예외를 던지지 않고 Host가 정상적으로
    /// 기동·종료되는지가 이 Step의 완료 기준입니다. 실제 기동 로직은 RN-01에서 채웁니다.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
