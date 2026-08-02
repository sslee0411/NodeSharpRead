namespace NodeSharp.Util.Messaging;

// 한글명: 예약 작업 핸들
/// <summary>
/// <see cref="AsyncScheduler.Schedule"/>이 등록한 작업 하나를 나타내는 핸들입니다. 이 핸들로 작업을
/// 잠깐 멈추거나(<see cref="Pause"/>) 다시 시작하거나(<see cref="Resume"/>) 완전히 취소(<see cref="Cancel"/>)할
/// 수 있고, 지금까지 몇 번 실행됐는지(<see cref="RunCount"/>)·마지막으로 어떤 예외가 났는지
/// (<see cref="LastError"/>)도 확인할 수 있습니다. lssLib.Messaging.AsyncScheduler 원본을 구조·이름
/// 그대로 포팅(복사)했습니다.
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서.
/// </summary>
/// <remarks>
/// 실제 반복 실행 루프는 이 클래스가 생성될 때 시작하는 백그라운드 <see cref="Task"/> 하나입니다 —
/// "InitialDelay만큼 기다림 → 콜백 실행 → RunCount 증가 → MaxRuns/Interval 조건 확인 → (반복이면)
/// Interval만큼 다시 기다림"을 <see cref="Cancel"/>이 호출되거나 반복 조건이 끝날 때까지 되풀이합니다.
/// <see cref="Pause"/> 중에는 짧은 간격(50ms)으로 "재개됐는지"만 확인하며 대기합니다 — 정밀한 타이머
/// 재시작 없이 가장 단순하게 구현한 것으로, 초 단위보다 훨씬 짧은 정밀도가 필요한 경우에는 맞지
/// 않을 수 있습니다.
/// </remarks>
public sealed class ScheduledTask
{
    private readonly Func<CancellationToken, Task> _callback;
    private readonly ScheduleOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private volatile bool _paused;

    /// <summary>이 작업의 이름(<see cref="ScheduleOptions.Name"/>).</summary>
    public string Name => _options.Name;

    /// <summary>지금까지 콜백이 실제로 실행 완료된 횟수입니다.</summary>
    public int RunCount { get; private set; }

    /// <summary><see cref="Cancel"/>이 호출되지 않아 아직 반복 중이면 <c>true</c>입니다(일시 정지 중이어도 취소되지 않았으면 true).</summary>
    public bool IsRunning => !_cts.IsCancellationRequested;

    /// <summary>가장 최근에 콜백이 던진 예외입니다. 아직 예외가 없었으면 <c>null</c>입니다.</summary>
    public Exception? LastError { get; private set; }

    internal ScheduledTask(Func<CancellationToken, Task> callback, ScheduleOptions options)
    {
        _callback = callback;
        _options = options;
        _loopTask = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        try
        {
            if (_options.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.InitialDelay, _cts.Token);
            }

            while (!_cts.IsCancellationRequested)
            {
                // 일시 정지 중이면 짧게 재확인만 반복 — Cancel되면 이 대기도 곧바로 빠져나온다.
                while (_paused && !_cts.IsCancellationRequested)
                {
                    await Task.Delay(50, _cts.Token);
                }

                if (_cts.IsCancellationRequested) break;

                try
                {
                    await _callback(_cts.Token);
                    RunCount++;
                }
                catch (OperationCanceledException)
                {
                    throw;   // Cancel()로 인한 정상 취소는 바깥 catch에서 조용히 종료 처리
                }
                catch (Exception ex)
                {
                    LastError = ex;
                    if (!_options.ContinueOnError) break;
                }

                if (_options.MaxRuns > 0 && RunCount >= _options.MaxRuns) break;
                if (_options.Interval <= TimeSpan.Zero) break;   // Interval이 없으면 1회성 작업

                await Task.Delay(_options.Interval, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancel() 호출로 인한 정상 종료 — 예외로 취급하지 않는다.
        }
    }

    /// <summary>다음 재개(<see cref="Resume"/>) 전까지 콜백 실행을 멈춥니다. 이미 실행 중인 콜백은 끝까지 마칩니다.</summary>
    public void Pause() => _paused = true;

    /// <summary><see cref="Pause"/>로 멈춘 작업을 다시 시작합니다.</summary>
    public void Resume() => _paused = false;

    /// <summary>이 작업을 완전히 취소합니다. 취소 후에는 <see cref="Resume"/>으로 되살릴 수 없습니다.</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>반복 루프가 완전히 끝날 때까지 기다립니다(<see cref="AsyncScheduler.StopAsync"/>가 사용).</summary>
    internal Task WaitForCompletionAsync() => _loopTask;
}
