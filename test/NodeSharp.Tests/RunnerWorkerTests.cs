using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeSharp.Runner;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// NodeSharp.Runner의 <see cref="Worker"/>(RN-B0)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-B0):
/// "DI 컨테이너만 구성된 빈 Worker Service가 예외 없이 기동·정상 종료되는지 확인".
/// </summary>
public class RunnerWorkerTests
{
    [Fact]
    public async Task 완료_기준_직접_검증__DI_컨테이너로_구성한_Worker_Host는_예외_없이_기동하고_정상_종료된다()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddHostedService<Worker>())
            .Build();

        await host.StartAsync();
        await host.StopAsync();

        // 여기까지 예외 없이 도달하면 완료 기준 충족 — 별도 Assert 불필요(예외 발생 시 테스트가 자동 실패).
    }

    [Fact]
    public async Task Worker는_ExecuteAsync에서_예외를_던지지_않는다()
    {
        var worker = new Worker();
        using var cts = new CancellationTokenSource();

        var task = worker.StartAsync(cts.Token);
        await task;
        await worker.StopAsync(CancellationToken.None);

        Assert.True(task.IsCompletedSuccessfully);
    }
}
