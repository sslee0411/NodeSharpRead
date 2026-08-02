using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeSharp.Runner;
using NodeSharp.Runner.Health;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// NodeSharp.Runner의 <see cref="Worker"/>(RN-B0)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-B0):
/// "DI 컨테이너만 구성된 빈 Worker Service가 예외 없이 기동·정상 종료되는지 확인".
/// (RN-04a) Worker 생성자가 <see cref="RunnerHealthState"/>를 주입받도록 바뀌어, 두 테스트 모두
/// DI 컨테이너/직접 생성 양쪽에서 이 의존성을 함께 준비하도록 갱신했습니다 — 테스트 로직·Assert
/// 자체는 RN-B0 때와 동일합니다. (RN-05a) Worker가 <see cref="ClockDriftMonitor"/>도 주입받도록
/// 바뀌어, 두 테스트 모두 가짜 reader를 가진 인스턴스를 함께 준비합니다 — 그래야 테스트가
/// 실제 w32tm.exe를 호출하지 않고 빠르고 결정적으로 끝납니다(이 개발 환경에는 w32tm 자체가 없음).
/// </summary>
public class RunnerWorkerTests
{
    private static ClockDriftMonitor NewFakeClockDriftMonitor() =>
        new(offsetReader: _ => Task.FromResult(0.0));

    [Fact]
    public async Task 완료_기준_직접_검증__DI_컨테이너로_구성한_Worker_Host는_예외_없이_기동하고_정상_종료된다()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<RunnerHealthState>();
                services.AddSingleton(NewFakeClockDriftMonitor());
                services.AddHostedService<Worker>();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();

        // 여기까지 예외 없이 도달하면 완료 기준 충족 — 별도 Assert 불필요(예외 발생 시 테스트가 자동 실패).
    }

    [Fact]
    public async Task Worker는_ExecuteAsync에서_예외를_던지지_않는다()
    {
        var worker = new Worker(new RunnerHealthState(), NewFakeClockDriftMonitor());
        using var cts = new CancellationTokenSource();

        var task = worker.StartAsync(cts.Token);
        await task;
        await worker.StopAsync(CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
    }
}
