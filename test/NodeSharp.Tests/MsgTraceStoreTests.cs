using NodeSharp.Contracts.Events;
using NodeSharp.Runner.Core;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-04) <see cref="MsgTraceStore"/>에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 LK-04)의
/// "Msg Trace로 에러 발생 노드와 해당 시점 Msg 내용까지 역추적 가능한지"를 이 클래스가 실제로
/// 뒷받침하는지 검증합니다: ① <see cref="FlowActivityEvent"/>가 msg.Id 기준으로 순서대로 누적되는지
/// ② 서로 다른 msg.Id는 서로 섞이지 않는지 ③ 추적된 적 없는 msg.Id는 <c>null</c>을 반환하는지
/// ④ <see cref="MsgTraceStore.MaxTrackedMessages"/>를 넘으면 가장 오래된 것부터 제거되는지
/// ⑤ 반환값이 내부 상태와 독립된 스냅샷인지(참조 공유로 인한 경합 방지). 각 테스트는
/// <see cref="RunnerTokenStoreTests"/>와 마찬가지로 <see cref="EventBusAdapter"/>에 매번 새
/// <see cref="EventBus"/>를 주입해 테스트 간 구독이 섞이지 않게 합니다(LK-02a 착수 중 발견한 원칙).
/// </summary>
public class MsgTraceStoreTests
{
    private static EventBusAdapter NewEventBus() => new(new EventBus());

    [Fact]
    public void FlowActivityEvent가_msgId_기준으로_시간순으로_누적된다()
    {
        var eventBus = NewEventBus();
        var store = new MsgTraceStore();
        using var subscription = store.Subscribe(eventBus);
        var t0 = DateTime.UtcNow;

        eventBus.Publish(new FlowActivityEvent("inject-1", 0, "function-1", "msg-1", t0));
        eventBus.Publish(new FlowActivityEvent("function-1", 0, "debug-1", "msg-1", t0.AddMilliseconds(5)));

        var trace = store.GetTrace("msg-1");

        Assert.NotNull(trace);
        Assert.Equal("msg-1", trace!.MsgId);
        Assert.Equal(2, trace.Steps.Count);
        Assert.Equal("inject-1", trace.Steps[0].FromNodeId);
        Assert.Equal("debug-1", trace.Steps[1].ToNodeId);
    }

    [Fact]
    public void 서로_다른_msgId는_서로_섞이지_않는다()
    {
        var eventBus = NewEventBus();
        var store = new MsgTraceStore();
        using var subscription = store.Subscribe(eventBus);

        eventBus.Publish(new FlowActivityEvent("inject-1", 0, "function-1", "msg-A", DateTime.UtcNow));
        eventBus.Publish(new FlowActivityEvent("inject-2", 0, "function-2", "msg-B", DateTime.UtcNow));

        var traceA = store.GetTrace("msg-A");
        var traceB = store.GetTrace("msg-B");

        Assert.Single(traceA!.Steps);
        Assert.Single(traceB!.Steps);
        Assert.Equal("function-1", traceA.Steps[0].ToNodeId);
        Assert.Equal("function-2", traceB.Steps[0].ToNodeId);
    }

    [Fact]
    public void 추적된_적_없는_msgId는_null을_반환한다()
    {
        var store = new MsgTraceStore();

        var trace = store.GetTrace("한번도-없었던-msgId");

        Assert.Null(trace);
    }

    [Fact]
    public void 상한을_넘으면_가장_오래된_msgId부터_제거된다()
    {
        var eventBus = NewEventBus();
        var store = new MsgTraceStore();
        using var subscription = store.Subscribe(eventBus);

        for (var i = 0; i < MsgTraceStore.MaxTrackedMessages + 1; i++)
        {
            eventBus.Publish(new FlowActivityEvent("a", 0, "b", $"msg-{i}", DateTime.UtcNow));
        }

        // msg-0은 상한을 넘기며 가장 먼저 제거되고, 가장 최근(msg-500)은 남아 있어야 한다.
        Assert.Null(store.GetTrace("msg-0"));
        Assert.NotNull(store.GetTrace($"msg-{MsgTraceStore.MaxTrackedMessages}"));
    }

    [Fact]
    public void GetTrace의_반환값은_내부_상태와_독립된_스냅샷이다()
    {
        var eventBus = NewEventBus();
        var store = new MsgTraceStore();
        using var subscription = store.Subscribe(eventBus);
        eventBus.Publish(new FlowActivityEvent("a", 0, "b", "msg-1", DateTime.UtcNow));

        var snapshot = store.GetTrace("msg-1");
        eventBus.Publish(new FlowActivityEvent("b", 0, "c", "msg-1", DateTime.UtcNow));

        // snapshot을 받은 "이후"에 발행된 이벤트는 이미 반환된 snapshot에 반영되지 않아야 한다
        // (반환값이 내부 List를 그대로 공유하면 여기서 2개가 됨 — MsgTraceStore 클래스 문서 "반환값은
        // 항상 복사본" 항목 참고).
        Assert.Single(snapshot!.Steps);
        Assert.Equal(2, store.GetTrace("msg-1")!.Steps.Count);
    }
}
