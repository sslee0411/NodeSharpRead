using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TagValueCache"/>/<see cref="DeviceMapPoller"/>(ED-D06b, 03번 개발 Step맵 — DeviceMapPoller
/// 배치 폴링)에 대한 단위 테스트입니다. 이 Step의 완료 기준("태그 10개 이상을 배치 폴링으로 묶으면
/// TagValueCache를 경유해 개별 폴링 대비 통신 횟수가 줄어드는지 확인")은 실제 PLC·IStructureService
/// 연결 여부와 무관하게 검증 가능하도록 설계되어(클래스 문서 참고), 여기서 xUnit만으로 완전히
/// 증명합니다(PD-01a/ED-D06a와 동일한 선례).
/// </summary>
public class DeviceMapPollerTests
{
    /// <summary>등록된 콜백·ownerId·interval을 그대로 기록만 하는 테스트 전용 <see cref="IScheduler"/>(실제 시간 경과 없이 결정적으로 검증하기 위함, InjectNodeTests와 동일한 취지).</summary>
    private sealed class FakeScheduler : IScheduler
    {
        public string? LastPeriodicOwnerId { get; private set; }
        public TimeSpan? LastInterval { get; private set; }
        public Func<Task>? LastCallback { get; private set; }
        public string? UnscheduledOwnerId { get; private set; }

        public void SchedulePeriodic(string ownerId, TimeSpan interval, Func<Task> callback)
        {
            LastPeriodicOwnerId = ownerId;
            LastInterval = interval;
            LastCallback = callback;
        }

        public void ScheduleCron(string ownerId, string cronExpression, Func<Task> callback)
        {
        }

        public void Unschedule(string ownerId) => UnscheduledOwnerId = ownerId;
    }

    [Fact]
    public void TagValueCache_Set한_값을_GetCached로_그대로_읽는다()
    {
        var cache = new TagValueCache();
        cache.Set("tag-1", 42.0);

        Assert.Equal(42.0, cache.GetCached("tag-1"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TagValueCache_갱신된_적_없는_태그는_null을_반환한다()
    {
        var cache = new TagValueCache();

        Assert.Null(cache.GetCached("존재하지-않는-태그"));
        Assert.False(cache.TryGetCached("존재하지-않는-태그", out _));
    }

    [Fact]
    public async Task PollOnceAsync는_BlockReadAction이_null이면_아무것도_하지_않는다()
    {
        var poller = new DeviceMapPoller
        {
            Id = "map-1",
            TagIds = new[] { "tag-1", "tag-2" },
        };

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, poller.Cache.Count);
    }

    [Fact]
    public async Task PollOnceAsync_한번_호출로_태그_10개가_한꺼번에_갱신되고_통신은_1회만_발생한다()
    {
        // 완료 기준의 핵심: 개별 폴링이었다면 태그 10개 = 통신 10회였을 것을, 배치 폴링은 통신 1회로
        // 태그 10개를 모두 캐시에 반영한다는 것을 직접 증명한다.
        var tagIds = Enumerable.Range(1, 10).Select(i => $"tag-{i}").ToArray();
        var callCount = 0;

        var poller = new DeviceMapPoller
        {
            Id = "map-1",
            TagIds = tagIds,
            BlockReadAction = (_) =>
            {
                Interlocked.Increment(ref callCount);
                IReadOnlyDictionary<string, object?> values =
                    tagIds.ToDictionary(id => id, id => (object?)(double.Parse(id.Split('-')[1]) * 10.0));
                return Task.FromResult(values);
            },
        };

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.Equal(10, poller.Cache.Count);
        for (var i = 1; i <= 10; i++)
        {
            Assert.Equal(i * 10.0, poller.Cache.GetCached($"tag-{i}"));
        }
    }

    [Fact]
    public async Task PollOnceAsync는_반환값에_없는_태그의_기존_캐시값을_지우지_않는다()
    {
        var poller = new DeviceMapPoller
        {
            Id = "map-1",
            TagIds = new[] { "tag-1", "tag-2" },
        };
        poller.Cache.Set("tag-2", 999.0); // 이전 폴링에서 이미 채워진 값(이번엔 응답에 없다고 가정)

        poller.BlockReadAction = (_) =>
        {
            IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["tag-1"] = 1.0 };
            return Task.FromResult(values);
        };

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1.0, poller.Cache.GetCached("tag-1"));
        Assert.Equal(999.0, poller.Cache.GetCached("tag-2")); // 유지됨
    }

    [Fact]
    public async Task GetCached는_Cache_GetCached와_동일하게_동작한다()
    {
        var poller = new DeviceMapPoller { Id = "map-1", TagIds = new[] { "tag-1" } };
        poller.BlockReadAction = (_) =>
        {
            IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["tag-1"] = 7.0 };
            return Task.FromResult(values);
        };

        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(7.0, poller.GetCached("tag-1"));
    }

    [Fact]
    public async Task StartAsync는_Scheduler에_Id를_ownerId로_PollInterval마다_등록한다()
    {
        var fake = new FakeScheduler();
        var poller = new DeviceMapPoller
        {
            Id = "map-42",
            TagIds = Array.Empty<string>(),
            PollInterval = TimeSpan.FromSeconds(3),
            Scheduler = fake,
        };

        await poller.StartAsync(CancellationToken.None);

        Assert.Equal("map-42", fake.LastPeriodicOwnerId);
        Assert.Equal(TimeSpan.FromSeconds(3), fake.LastInterval);
        Assert.NotNull(fake.LastCallback);
    }

    [Fact]
    public async Task StartAsync가_등록한_콜백을_직접_호출하면_PollOnceAsync와_동일하게_캐시가_갱신된다()
    {
        var fake = new FakeScheduler();
        var callCount = 0;
        var poller = new DeviceMapPoller
        {
            Id = "map-1",
            TagIds = new[] { "tag-1" },
            Scheduler = fake,
            BlockReadAction = (_) =>
            {
                Interlocked.Increment(ref callCount);
                IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["tag-1"] = 5.0 };
                return Task.FromResult(values);
            },
        };

        await poller.StartAsync(CancellationToken.None);
        await fake.LastCallback!();

        Assert.Equal(1, callCount);
        Assert.Equal(5.0, poller.GetCached("tag-1"));
    }

    [Fact]
    public async Task StopAsync는_Scheduler_Unschedule을_Id로_호출한다()
    {
        var fake = new FakeScheduler();
        var poller = new DeviceMapPoller { Id = "map-7", TagIds = Array.Empty<string>(), Scheduler = fake };
        await poller.StartAsync(CancellationToken.None);

        await poller.StopAsync();

        Assert.Equal("map-7", fake.UnscheduledOwnerId);
    }
}
