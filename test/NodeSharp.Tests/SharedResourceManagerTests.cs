using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SharedResourceManager"/>(RT-10, 02번 문서 2번 탭 카드2 — 참조 카운트 기반 공유 리소스
/// 관리)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-10): 같은 공유 리소스를 참조하는 노드 2개를
/// 배포한 뒤 하나만 종료해도 리소스가 유지되고, 참조 카운트가 0이 될 때만 실제 해제되는지 확인. 동시
/// 호출 시 <c>factory</c>가 정확히 한 번만 실행되는지도 함께 검증한다(카드2 원본 의사코드의 레이스
/// 컨디션을 수정한 부분에 대한 직접 검증, <see cref="SharedResourceManager"/> XML 주석 참고).
/// </summary>
public class SharedResourceManagerTests
{
    /// <summary>StartAsync/StopAsync 호출 횟수를 기록하는 테스트 전용 공유 리소스.</summary>
    private sealed class FakeService : ISharedServiceNode
    {
        public string Id { get; }
        public int StartCount;
        public int StopCount;

        public FakeService(string id) => Id = id;

        public Task StartAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref StartCount);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Interlocked.Increment(ref StopCount);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task 같은_id로_여러_번_Acquire해도_StartAsync는_최초_1회만_호출된다()
    {
        var manager = new SharedResourceManager();
        var service = new FakeService("srv-5000");

        var s1 = await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);
        var s2 = await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);
        var s3 = await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);

        Assert.Equal(1, service.StartCount);
        Assert.Same(s1, s2);
        Assert.Same(s2, s3);
    }

    [Fact]
    public async Task 참조가_남아있으면_Release해도_StopAsync는_호출되지_않는다()
    {
        var manager = new SharedResourceManager();
        var service = new FakeService("srv-5000");
        await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);
        await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);   // 참조 2

        await manager.ReleaseAsync("srv-5000");   // 참조 2 → 1

        Assert.Equal(0, service.StopCount);
    }

    [Fact]
    public async Task 마지막_참조가_해제될_때만_StopAsync가_호출된다()
    {
        var manager = new SharedResourceManager();
        var service = new FakeService("srv-5000");
        await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);
        await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);   // 참조 2

        await manager.ReleaseAsync("srv-5000");   // 참조 2 → 1
        Assert.Equal(0, service.StopCount);

        await manager.ReleaseAsync("srv-5000");   // 참조 1 → 0

        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task 서로_다른_id는_참조_카운트가_독립적으로_관리된다()
    {
        var manager = new SharedResourceManager();
        var a = new FakeService("srv-a");
        var b = new FakeService("srv-b");

        await manager.AcquireAsync("srv-a", () => a, CancellationToken.None);
        await manager.AcquireAsync("srv-b", () => b, CancellationToken.None);
        await manager.ReleaseAsync("srv-a");   // a는 0으로, b는 영향 없음

        Assert.Equal(1, a.StopCount);
        Assert.Equal(0, b.StopCount);
    }

    [Fact]
    public async Task 등록되지_않은_id를_Release해도_예외_없이_무시된다()
    {
        var manager = new SharedResourceManager();

        var ex = await Record.ExceptionAsync(() => manager.ReleaseAsync("no-such-id"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task 재해제해도_StopAsync가_다시_호출되지_않는다()
    {
        var manager = new SharedResourceManager();
        var service = new FakeService("srv-5000");
        await manager.AcquireAsync("srv-5000", () => service, CancellationToken.None);

        await manager.ReleaseAsync("srv-5000");   // 참조 1 → 0, StopAsync 호출
        await manager.ReleaseAsync("srv-5000");   // 이미 제거된 id — 무시

        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task 같은_id로_동시에_여러_번_Acquire해도_factory는_정확히_1번만_실행된다()
    {
        // 완료 기준 직접 검증 + 카드2 원본 의사코드의 레이스 컨디션 수정 검증(SharedResourceManager XML
        // 주석 참고) — lock 밖에서 factory/StartAsync를 실행하는 원본 구조였다면 동시 호출 시 factory가
        // 여러 번 실행되고 먼저 만든 인스턴스가 유실될 수 있었다.
        var manager = new SharedResourceManager();
        var factoryCallCount = 0;
        FakeService Factory()
        {
            Interlocked.Increment(ref factoryCallCount);
            return new FakeService("srv-concurrent");
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => manager.AcquireAsync("srv-concurrent", Factory, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, factoryCallCount);
        Assert.True(results.All(r => ReferenceEquals(r, results[0])));   // 전부 같은 인스턴스
    }
}
