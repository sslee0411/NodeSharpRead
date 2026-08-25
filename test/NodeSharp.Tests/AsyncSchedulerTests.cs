using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="AsyncScheduler"/>/<see cref="ScheduledTask"/>(NodeSharp.Util로 포팅된
/// lssLib.Messaging.AsyncScheduler)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-08):
/// AsyncSchedulerAdapter로 등록한 주기 작업이 while-true 없이 정확한 간격으로 실행되는지 확인. 시간에
/// 민감한 테스트라 간격을 짧게(수십 ms) 두고 여유 있는 범위로 검증합니다 — 정확히 몇 번인지보다
/// "반복되는지/멈추는지"의 방향성을 확인하는 데 중점을 둡니다.
/// ★ 수정(2026-08-25, 사용자 Windows dotnet test 실행에서 발견): 원래 대기 시간이 160ms 등으로 짧아,
/// 전체 테스트 스위트를 한 번에 돌리면(xUnit이 여러 테스트 클래스를 병렬 실행 + 이 파일 자체도 매
/// Task.Delay 호출마다 CLR ThreadPool을 공유) 스레드 풀이 아직 충분히 예열되지 않은 실행 초반 구간에
/// 이 타이머 콜백들이 지연돼 "0번 실행됨" 같은 극단적 값까지 나올 수 있음을 실제 실행 결과로 확인 —
/// 로직 자체의 결함이 아니라 병렬 테스트 실행 환경의 타이밍 여유 부족이 원인. 모든 대기 시간을
/// 큰 폭으로 늘려(대략 4~5배) 스레드 풀 예열 지연을 흡수하도록 수정 — 완료 기준이 요구하는 "반복
/// 되는지/멈추는지" 방향성 검증 자체는 그대로 유지.
/// </summary>
public class AsyncSchedulerTests
{
    [Fact]
    public async Task ScheduleRecurring은_간격마다_반복_실행된다()
    {
        var scheduler = new AsyncScheduler();
        var task = scheduler.ScheduleRecurring(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "poll");

        await Task.Delay(1000);

        Assert.True(task.RunCount >= 3, $"1000ms 동안 20ms 간격이면 최소 3번은 실행돼야 하는데 {task.RunCount}번 실행됨");
        task.Cancel();
    }

    [Fact]
    public async Task ScheduleOnce는_딱_한_번만_실행된다()
    {
        var scheduler = new AsyncScheduler();
        var task = scheduler.ScheduleOnce(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "once");

        await Task.Delay(400);
        var afterFirstWait = task.RunCount;
        await Task.Delay(400);

        Assert.Equal(1, afterFirstWait);
        Assert.Equal(1, task.RunCount);   // 시간이 더 지나도 다시 실행되지 않음
    }

    [Fact]
    public async Task Cancel하면_그_이후로는_실행되지_않는다()
    {
        var scheduler = new AsyncScheduler();
        var task = scheduler.ScheduleRecurring(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "cancel-test");

        await Task.Delay(400);
        task.Cancel();
        var countAtCancel = task.RunCount;
        await Task.Delay(400);

        // Cancel 직후 이미 진행 중이던 한 회차는 끝까지 갈 수 있어 +1까지는 허용한다.
        Assert.InRange(task.RunCount, countAtCancel, countAtCancel + 1);
        Assert.False(task.IsRunning);
    }

    [Fact]
    public async Task Pause하면_멈추고_Resume하면_다시_실행된다()
    {
        var scheduler = new AsyncScheduler();
        var task = scheduler.ScheduleRecurring(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "pause-test");

        await Task.Delay(400);
        task.Pause();
        var countAtPause = task.RunCount;
        await Task.Delay(400);
        var countWhilePaused = task.RunCount;

        Assert.InRange(countWhilePaused, countAtPause, countAtPause + 1);   // 일시 정지 중엔 늘어나지 않음(진행 중이던 1회 제외)

        task.Resume();
        await Task.Delay(400);

        Assert.True(task.RunCount > countWhilePaused, "Resume 이후에는 다시 늘어나야 함");
        task.Cancel();
    }

    [Fact]
    public async Task 콜백이_예외를_던지면_LastError에_기록되고_ContinueOnError_기본값이라_계속_실행된다()
    {
        var scheduler = new AsyncScheduler();
        var task = scheduler.Schedule(
            _ => throw new InvalidOperationException("일부러 던진 예외"),
            new ScheduleOptions { Name = "error-test", InitialDelay = TimeSpan.FromMilliseconds(10), Interval = TimeSpan.FromMilliseconds(20) });

        await Task.Delay(400);

        Assert.NotNull(task.LastError);
        Assert.IsType<InvalidOperationException>(task.LastError);
        Assert.Equal(0, task.RunCount);   // 예외가 나면 그 회차는 "성공적으로 실행됨"으로 세지 않음
        task.Cancel();
    }

    [Fact]
    public async Task ContinueOnError가_false면_예외_이후_반복을_멈춘다()
    {
        var attemptCount = 0;
        var scheduler = new AsyncScheduler();
        scheduler.Schedule(
            _ =>
            {
                Interlocked.Increment(ref attemptCount);
                throw new InvalidOperationException("한 번만 시도돼야 함");
            },
            new ScheduleOptions
            {
                Name = "stop-on-error",
                InitialDelay = TimeSpan.FromMilliseconds(10),
                Interval = TimeSpan.FromMilliseconds(20),
                ContinueOnError = false,
            });

        await Task.Delay(400);

        Assert.Equal(1, attemptCount);   // 첫 예외 이후 더 이상 시도되지 않음
    }

    [Fact]
    public async Task ScheduleDailyAt은_InitialDelay를_오늘_또는_내일_그_시각까지로_계산한다()
    {
        var scheduler = new AsyncScheduler();

        // ★(테스트 버그 수정) DateTime.Now.TimeOfDay를 미리 찍어서 그대로 넘기면, ScheduleDailyAt
        // 내부에서 다시 읽는 DateTime.Now가 항상 그보다 조금이라도 더 늦은 시각이라 "next <= now"가
        // 거의 100% 참이 되어 다음 실행이 내일로 밀린다(InitialDelay가 즉시가 아니라 거의 24시간이 됨,
        // 그래서 아래 300ms 대기 안에 한 번도 실행되지 못해 항상 실패했다). 이 테스트는 "곧 도래하는
        // 시각"을 검증하는 게 목적이므로, 미래로 살짝(50ms) 민 시각을 넘겨 확실히 오늘 안에(그리고
        // 거의 즉시) 실행되도록 한다.
        var timeOfDay = DateTime.Now.AddMilliseconds(50).TimeOfDay;

        var task = scheduler.ScheduleDailyAt(timeOfDay, _ => Task.CompletedTask, "daily");
        await Task.Delay(600);

        Assert.True(task.RunCount >= 1);
        task.Cancel();
    }

    [Fact]
    public async Task StopAsync은_등록된_모든_작업을_취소한다()
    {
        var scheduler = new AsyncScheduler();
        var t1 = scheduler.ScheduleRecurring(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "a");
        var t2 = scheduler.ScheduleRecurring(TimeSpan.FromMilliseconds(20), _ => Task.CompletedTask, "b");

        await scheduler.StopAsync();

        Assert.False(t1.IsRunning);
        Assert.False(t2.IsRunning);
    }
}
