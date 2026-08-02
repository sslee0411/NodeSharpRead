using System.Collections.Concurrent;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="EventBus"/>(NodeSharp.Util로 포팅된 lssLib.Messaging.EventBus)와 이를 감싸는
/// <see cref="EventBusAdapter"/>(RT-07, 02번 설계 문서 3번 탭 카드5 <c>IEventBus</c>)에 대한 단위
/// 테스트입니다. 완료 기준(03번 Step맵 RT-07): 포팅된 EventBus의 Subscribe/Publish 왕복이 원본과
/// 동일하게 동작하는지 확인. 테스트마다 새 <see cref="EventBus"/> 인스턴스를 만들어 써서, 앱 전체가
/// 공유하는 <see cref="EventBus.Instance"/>(싱글턴)의 구독 상태가 테스트끼리 섞이지 않게 합니다.
/// </summary>
public class EventBusTests
{
    private sealed record PingEvent(string Message) : EventMessage;
    private sealed record OtherEvent(int Value) : EventMessage;

    [Fact]
    public void Subscribe_후_Publish하면_핸들러가_같은_값으로_호출된다()
    {
        var bus = new EventBus();
        string? received = null;

        bus.Subscribe<PingEvent>(e => received = e.Message);
        bus.Publish(new PingEvent("hello"));

        Assert.Equal("hello", received);
    }

    [Fact]
    public void 구독자가_여러_명이면_모두_호출된다()
    {
        var bus = new EventBus();
        var callCount = 0;

        bus.Subscribe<PingEvent>(_ => callCount++);
        bus.Subscribe<PingEvent>(_ => callCount++);
        bus.Subscribe<PingEvent>(_ => callCount++);
        bus.Publish(new PingEvent("hi"));

        Assert.Equal(3, callCount);
    }

    [Fact]
    public void 다른_이벤트_타입_구독자는_호출되지_않는다()
    {
        var bus = new EventBus();
        var pingCalled = false;
        var otherCalled = false;

        bus.Subscribe<PingEvent>(_ => pingCalled = true);
        bus.Subscribe<OtherEvent>(_ => otherCalled = true);
        bus.Publish(new PingEvent("hi"));

        Assert.True(pingCalled);
        Assert.False(otherCalled);
    }

    [Fact]
    public void Dispose하면_그_이후로는_이벤트를_받지_않는다()
    {
        var bus = new EventBus();
        var callCount = 0;

        var sub = bus.Subscribe<PingEvent>(_ => callCount++);
        bus.Publish(new PingEvent("1"));
        sub.Dispose();
        bus.Publish(new PingEvent("2"));

        Assert.Equal(1, callCount);   // 두 번째 발행은 구독 해제 후라 반영되지 않음
    }

    [Fact]
    public void Dispose를_두_번_호출해도_예외가_나지_않는다()
    {
        var bus = new EventBus();
        var sub = bus.Subscribe<PingEvent>(_ => { });

        var ex = Record.Exception(() =>
        {
            sub.Dispose();
            sub.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishAsync는_비동기_핸들러가_모두_끝날_때까지_기다린다()
    {
        // 버그 수정(2026-08-02): completedOrder를 List<int>로 두면, Task.Delay(30)/Task.Delay(10)
        // 두 핸들러의 완료 콜백이 서로 다른 스레드풀 스레드에서 거의 동시에 Add를 호출할 수 있는데
        // List<T>는 스레드 안전하지 않아 드물게 한쪽 Add가 씹혀 Count가 2가 아니라 1로 나오는
        // 간헐적 실패가 있었다(EventBus.PublishAsync 자체는 Task.WhenAll로 두 핸들러를 정확히 다
        // 기다리는 것을 코드로 확인 — 프로덕션 버그가 아니라 이 테스트의 집계 방식 버그). 여러
        // 스레드에서 동시에 Add해도 안전한 ConcurrentBag<int>로 교체해 수정.
        var bus = new EventBus();
        var completedOrder = new ConcurrentBag<int>();

        bus.SubscribeAsync<PingEvent>(async _ =>
        {
            await Task.Delay(30);
            completedOrder.Add(1);
        });
        bus.SubscribeAsync<PingEvent>(async _ =>
        {
            await Task.Delay(10);
            completedOrder.Add(2);
        });

        await bus.PublishAsync(new PingEvent("hi"));

        // PublishAsync가 실제로 두 핸들러를 모두 기다렸다면, 반환 시점엔 둘 다 완료돼 있어야 한다.
        Assert.Equal(2, completedOrder.Count);
    }

    [Fact]
    public void IEventHandler_구현체로도_구독할_수_있다()
    {
        var bus = new EventBus();
        var handler = new RecordingHandler();

        bus.Subscribe<PingEvent>(handler);
        bus.Publish(new PingEvent("via-handler"));

        Assert.Equal("via-handler", handler.LastMessage);
    }

    [Fact]
    public void EventBusAdapter는_IEventBus로_동일하게_동작한다()
    {
        var innerBus = new EventBus();
        IEventBus adapter = new EventBusAdapter(innerBus);
        string? received = null;

        var sub = adapter.Subscribe<PingEvent>(e => received = e.Message);
        adapter.Publish(new PingEvent("adapter"));

        Assert.Equal("adapter", received);

        sub.Dispose();
        adapter.Publish(new PingEvent("after-dispose"));
        Assert.Equal("adapter", received);   // 해제 후 발행은 반영되지 않음
    }

    [Fact]
    public void EventMessage의_Timestamp는_생성_시점에_자동으로_채워진다()
    {
        var before = DateTime.UtcNow;
        var evt = new PingEvent("hi");
        var after = DateTime.UtcNow;

        Assert.InRange(evt.Timestamp, before.AddSeconds(-1), after.AddSeconds(1));
    }

    private sealed class RecordingHandler : IEventHandler<PingEvent>
    {
        public string? LastMessage { get; private set; }

        public Task HandleAsync(PingEvent e)
        {
            LastMessage = e.Message;
            return Task.CompletedTask;
        }
    }
}
