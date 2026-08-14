using NodeSharp.Runner.Core;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowFileWatcher"/>(LK-01)에 대한 테스트입니다. 완료 기준(03번 Step맵 LK-01): ".signal
/// 파일 변경을 FileSystemWatcher가 감지해 자동 재배포가 트리거되는지 확인" 중 "감지" 부분을 이
/// 클래스가 직접 검증합니다("재배포" 부분은 <see cref="FlowDeployer.RedeployAsync"/>를 대상으로
/// <c>FlowDeployerTests</c>가 별도로 다룸). 실제 파일 I/O·타이머를 쓰는 타이밍 테스트라 디바운스를
/// 짧게(수십~수백ms) 주고, InjectNodeTests의 기존 관례처럼 대기 시간에 충분한(수 배) 여유를 둡니다.
/// </summary>
public class FlowFileWatcherTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nodesharp-flowfilewatcher-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task 완료_기준_직접_검증__signal_파일이_생성되면_콜백이_호출된다()
    {
        var dir = NewTempDir();
        try
        {
            var tcs = new TaskCompletionSource();
            using var watcher = new FlowFileWatcher(dir, ct =>
            {
                tcs.TrySetResult();
                return Task.CompletedTask;
            }, debounce: TimeSpan.FromMilliseconds(50));

            await File.WriteAllTextAsync(Path.Combine(dir, "flows.json.signal"), DateTime.UtcNow.ToString("O"));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(tcs.Task, completed); // 5초 안에 콜백이 호출돼야 함(디바운스 50ms 대비 100배 여유)
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__signal_파일이_이미_있는_상태에서_다시_써도_콜백이_호출된다()
    {
        // Created가 아니라 Changed 경로도 확실히 잡히는지 확인 — 두 번째 저장부터는 파일이 이미 있어
        // FileSystemWatcher.Changed 이벤트로 감지된다(FlowFileWatcher.cs XML 문서의 두 이벤트 구독 근거).
        var dir = NewTempDir();
        try
        {
            var signalPath = Path.Combine(dir, "flows.json.signal");
            await File.WriteAllTextAsync(signalPath, "최초 저장");

            var tcs = new TaskCompletionSource();
            using var watcher = new FlowFileWatcher(dir, ct =>
            {
                tcs.TrySetResult();
                return Task.CompletedTask;
            }, debounce: TimeSpan.FromMilliseconds(50));

            await Task.Delay(100); // watcher가 완전히 EnableRaisingEvents=true 상태가 되도록 약간 대기
            await File.WriteAllTextAsync(signalPath, "재저장");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(tcs.Task, completed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task signal_파일이_디바운스_시간_안에_여러_번_바뀌어도_콜백은_한_번만_호출된다()
    {
        var dir = NewTempDir();
        try
        {
            var callCount = 0;
            var signalPath = Path.Combine(dir, "flows.json.signal");
            using var watcher = new FlowFileWatcher(dir, ct =>
            {
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            }, debounce: TimeSpan.FromMilliseconds(200));

            for (var i = 0; i < 5; i++)
            {
                await File.WriteAllTextAsync(signalPath, $"저장 {i}");
                await Task.Delay(20); // 디바운스 창(200ms)보다 훨씬 짧은 간격으로 연속 기록
            }

            await Task.Delay(TimeSpan.FromSeconds(2)); // 디바운스 종료 + 콜백 실행까지 충분히 대기(10배 여유)

            Assert.Equal(1, callCount); // 5번의 원시 이벤트가 1번의 콜백으로 합쳐짐(디바운스)
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 콜백이_예외를_던져도_Watcher는_계속_동작하고_다음_신호를_받는다()
    {
        // (LK-01) 콜백 예외 격리 — FlowFileWatcher.cs XML 문서의 "콜백 예외 격리" 근거를 직접 검증.
        var dir = NewTempDir();
        try
        {
            var callCount = 0;
            var signalPath = Path.Combine(dir, "flows.json.signal");
            using var watcher = new FlowFileWatcher(dir, ct =>
            {
                Interlocked.Increment(ref callCount);
                throw new InvalidOperationException("의도적인 테스트 예외");
            }, debounce: TimeSpan.FromMilliseconds(50));

            await File.WriteAllTextAsync(signalPath, "첫 번째");
            await Task.Delay(500); // 첫 콜백이 예외를 던지고 끝날 때까지 대기(디바운스 50ms 대비 10배 여유)

            await File.WriteAllTextAsync(signalPath, "두 번째");
            await Task.Delay(500);

            Assert.Equal(2, callCount); // 첫 콜백이 예외를 던졌어도 두 번째 신호가 정상적으로 다시 처리됨
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
