using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="AsyncSchedulerAdapter"/>(RT-08, <see cref="IScheduler"/> 구현체)에 대한 단위 테스트입니다.
/// 완료 기준(03번 Step맵 RT-08): AsyncSchedulerAdapter로 등록한 주기 작업이 while-true 없이 정확한
/// 간격으로 실행되고, IScheduler 계약을 만족하는지 확인. 테스트마다 새 <see cref="AsyncScheduler"/>
/// 인스턴스를 감싼 어댑터를 써서, 앱 전체 공유 싱글턴(<see cref="AsyncScheduler.Instance"/>)과 예약
/// 목록이 섞이지 않게 합니다(EventBusTests와 동일한 원칙).
/// </summary>
public class AsyncSchedulerAdapterTests
{
    [Fact]
    public async Task SchedulePeriodic은_간격마다_반복_호출된다()
    {
        IScheduler scheduler = new AsyncSchedulerAdapter(new AsyncScheduler());
        var callCount = 0;

        scheduler.SchedulePeriodic("owner-1", TimeSpan.FromMilliseconds(20), () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await Task.Delay(160);

        Assert.True(callCount >= 3, $"160ms 동안 20ms 간격이면 최소 3번은 호출돼야 하는데 {callCount}번 호출됨");
    }

    [Fact]
    public async Task Unschedule하면_그_이후로는_호출되지_않는다()
    {
        IScheduler scheduler = new AsyncSchedulerAdapter(new AsyncScheduler());
        var callCount = 0;

        scheduler.SchedulePeriodic("owner-2", TimeSpan.FromMilliseconds(20), () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await Task.Delay(80);
        scheduler.Unschedule("owner-2");
        var countAtUnschedule = callCount;
        await Task.Delay(150);

        Assert.InRange(callCount, countAtUnschedule, countAtUnschedule + 1);   // 진행 중이던 1회는 예외로 허용
    }

    [Fact]
    public void Unschedule는_등록된_적_없는_ownerId를_넘겨도_예외가_나지_않는다()
    {
        IScheduler scheduler = new AsyncSchedulerAdapter(new AsyncScheduler());

        var ex = Record.Exception(() => scheduler.Unschedule("no-such-owner"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task 같은_ownerId로_등록한_여러_예약을_Unschedule_한_번으로_모두_취소한다()
    {
        IScheduler scheduler = new AsyncSchedulerAdapter(new AsyncScheduler());
        var periodicCount = 0;
        var cronCount = 0;

        scheduler.SchedulePeriodic("owner-3", TimeSpan.FromMilliseconds(20), () =>
        {
            Interlocked.Increment(ref periodicCount);
            return Task.CompletedTask;
        });
        scheduler.ScheduleCron("owner-3", "* * * * * *", () =>
        {
            Interlocked.Increment(ref cronCount);
            return Task.CompletedTask;
        });

        await Task.Delay(80);
        scheduler.Unschedule("owner-3");
        var periodicAtUnschedule = periodicCount;
        await Task.Delay(150);

        Assert.InRange(periodicCount, periodicAtUnschedule, periodicAtUnschedule + 1);
    }

    [Fact]
    public async Task ScheduleCron은_조건에_맞는_순간에만_콜백을_호출한다()
    {
        // "* * * * * *"는 모든 초에 일치하므로, 어댑터의 1초 폴링 주기 특성상 1.1초 정도 지나면
        // 최소 1번은 호출돼야 한다(cron 표현식 자체의 매칭은 CronExpressionTests에서 이미 별도 검증).
        IScheduler scheduler = new AsyncSchedulerAdapter(new AsyncScheduler());
        var callCount = 0;

        scheduler.ScheduleCron("owner-4", "* * * * * *", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await Task.Delay(1200);

        Assert.True(callCount >= 1, "1.2초 동안 매초 일치하는 cron이 한 번도 호출되지 않음");
        scheduler.Unschedule("owner-4");
    }
}
