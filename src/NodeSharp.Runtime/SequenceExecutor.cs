using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 시퀀스 실행기
/// 역활 및 기능 : SequenceDefinition의 SequenceStepDto 목록을 순서·타임아웃·실패시이동 규칙대로 순차 실행하는 엔진
///
/// <see cref="SequenceDefinition"/>(<c>sequences.json</c>으로 저장될 "단계형 시퀀스" 정의)의
/// <see cref="SequenceStepDto"/> 목록을 순서대로 실행합니다 — 인터록·기동정지 절차처럼 Flow의
/// 이벤트 기반(<c>FlowEngine</c>) 방식보다 순서/타임아웃/실패분기가 엄격한 시나리오용입니다
/// (<see cref="SequenceDefinition"/> XML 문서 참고).
/// 설계 근거: 02번 문서 11번 탭 카드 5(Sequence 설계).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>(SQ-01) 범위 축소 — 자체 모델 기반 구현</b>: 사용자 확인(2026-09 세션, "SQ-01 범위" 질문 —
/// "자체 모델 기반 구현(권장)")에 따라 lssLib.Sequence(SequenceGroup/StepExecutionMode/ISequenceStep/
/// SequenceControllerBase — 그룹·병렬 실행 개념)를 그대로 이식하는 대신, 이미 Contracts에 있는
/// <see cref="SequenceDefinition"/>/<see cref="SequenceStepDto"/> 모델(평면 단계 목록·NCalc 진입조건·
/// OnFailStepId/OnTimeoutStepId 분기)을 그대로 실행하는 네이티브 엔진으로 구현했습니다 — 두 모델이
/// 1:1로 대응하지 않아(그룹·병렬 개념이 Contracts 모델에 없음) lssLib.Sequence 이식은 설계 변경이
/// 동반되는 별도 판단이 필요하다고 보았습니다(<c>ISequenceStepAction</c>/<c>ISequenceTriggerEvaluator</c>
/// XML 문서의 "범위 축소" 항목도 함께 참고 — 동작(ActionType)·진입조건(TriggerExpression) 평가는 각각
/// 전략 패턴 인터페이스로 뽑아 lssLib.Sequence의 "타입명으로 스텝을 식별한다"는 원래 의도를 최대한
/// 보존).</item>
/// <item><b>단계 식별</b>: <see cref="SequenceStepDto"/>에는 별도 Id 필드가 없고, <see cref="SequenceStepDto.OnFailStepId"/>/
/// <see cref="SequenceStepDto.OnTimeoutStepId"/> XML 문서가 "이동할 단계의 이름"이라고 명시하므로
/// <see cref="SequenceStepDto.Name"/>을 식별자로 씁니다(같은 시퀀스 안에서 Name은 고유해야 함 —
/// 생성자가 중복 Name을 <see cref="ArgumentException"/>으로 거부).</item>
/// <item><b>단계 1개의 처리 흐름</b>: 진입조건(<see cref="ISequenceTriggerEvaluator"/>)이 참이 될 때까지
/// <c>pollIntervalMs</c> 간격으로 재확인 → 참이 되면 동작(<see cref="ISequenceStepAction"/>, ActionType이
/// 비어있거나 등록되지 않았으면 즉시 성공 처리하는 무동작)을 실행 → 성공하면 <see cref="SequenceStepDto.Order"/>
/// 기준 다음 단계로, 실패(동작이 <c>false</c> 반환 또는 예외)하면 <see cref="SequenceStepDto.OnFailStepId"/>로,
/// 진입조건 대기+동작 실행을 합친 전체 시간이 <see cref="SequenceStepDto.TimeoutMs"/>(0이면 무제한)를
/// 넘기면 <see cref="SequenceStepDto.OnTimeoutStepId"/>로 분기합니다. 분기 대상이 없으면
/// <see cref="SequenceState.Faulted"/>로 시퀀스 전체를 종료합니다 — 카드5 원본 의사코드가 "타임아웃 시
/// 실패 처리 후 분기"라고만 서술해 진입조건 대기 시간과 동작 실행 시간을 분리할지 근거가 없어, 둘을
/// 합친 예산으로 해석한 판단입니다(운영자 입장에서 "이 단계가 몇 초 안에 끝나야 하는가"가 자연스러운
/// 해석).</item>
/// <item><b>알람 자동 안전정지</b>: <see cref="TagAlarmEvents.AlarmRaisedEvent"/> 예제 코드(TagAlarmEvents.cs
/// remarks)가 이미 "<see cref="SequenceDefinition.WatchedTagIds"/>에 포함되고 <see cref="AlarmLevel.HH"/>면
/// <c>AbortAsync()</c>"를 명시적으로 예고해뒀으므로, 이 Step에서 그대로 구현했습니다(추가 확인 불필요 —
/// 이미 확정된 설계).</item>
/// <item><b>일시정지</b>: <see cref="SequenceState.Paused"/> XML 문서가 "사용자가 PauseAsync()를 호출해
/// 일시정지"라고 이미 명시하지만, SQ-01의 완료 기준(순서·타임아웃·실패시이동)은 일시정지를 요구하지
/// 않습니다 — <see cref="Pause"/>/<see cref="Resume"/>는 매 단계 진입 직전에만 확인하는 최소 구현(진행
/// 중인 단계 도중에는 다음 단계로 넘어가기 전까지 즉시 멈추지 않음)으로 두고, 정밀한 즉시 일시정지는
/// 필요해지는 후속 Step(SQ-02 이후 Sequence Editor 연동)에서 재검토합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var openValve = new SequenceStepDto(0, "밸브 열기", "true", "PlcWriteStep",
///     new Dictionary&lt;string, object?&gt; { ["tagId"] = "tag-valve1", ["value"] = true },
///     TimeoutMs: 5000, OnFailStepId: "safe-stop", OnTimeoutStepId: "safe-stop");
/// var startPump = new SequenceStepDto(1, "펌프 기동", "tag-valve1 == true", "PlcWriteStep",
///     new Dictionary&lt;string, object?&gt; { ["tagId"] = "tag-pump1", ["value"] = true },
///     TimeoutMs: 3000, OnFailStepId: "safe-stop");
/// var safeStop = new SequenceStepDto(2, "safe-stop", "true", "", new Dictionary&lt;string, object?&gt;());
///
/// var def = new SequenceDefinition("seq-1", "1호기 펌프 기동 절차",
///     new[] { openValve, startPump, safeStop }, new[] { "tag-pressure" });
///
/// var actions = new Dictionary&lt;string, ISequenceStepAction&gt; { ["PlcWriteStep"] = new PlcWriteStepAction() };
/// var executor = new SequenceExecutor(def, eventBus, actions,
///     resolveVariable: tagId =&gt; tagValueCache.GetCached(tagId));
///
/// var finalState = await executor.RunAsync();   // SequenceState.Completed(모두 성공) 또는 Faulted
/// </code>
/// </example>
public sealed class SequenceExecutor : IDisposable
{
    /// <summary><see cref="SequenceStepDto.ActionType"/>이 이 값이거나 비어있으면 등록된 동작 없이 즉시 성공 처리합니다(진입조건만 있는 대기/분기 전용 단계용).</summary>
    public const string NoOpActionType = "NoOp";

    private enum StepOutcome
    {
        Success,
        Failed,
        TimedOut,
        Aborted,
    }

    private readonly SequenceDefinition _definition;
    private readonly IEventBus _eventBus;
    private readonly IReadOnlyDictionary<string, ISequenceStepAction> _actions;
    private readonly ISequenceTriggerEvaluator _triggerEvaluator;
    private readonly Func<string, object?> _resolveVariable;
    private readonly int _pollIntervalMs;
    private readonly List<SequenceStepDto> _orderedSteps;
    private readonly Dictionary<string, SequenceStepDto> _stepsByName;
    private readonly IDisposable? _alarmSubscription;

    private CancellationTokenSource? _abortCts;

    /// <summary>이 실행기가 실행 중인 시퀀스 정의입니다.</summary>
    public SequenceDefinition Definition => _definition;

    /// <summary>현재 상태(<see cref="SequenceState"/>) — <see cref="RunAsync"/> 호출 전에는 항상 <see cref="SequenceState.Idle"/>.</summary>
    public SequenceState State { get; private set; } = SequenceState.Idle;

    /// <summary>현재(또는 마지막으로) 실행 중이던 단계의 <see cref="SequenceStepDto.Name"/>. 아직 시작 전이면 <c>null</c>.</summary>
    public string? CurrentStepName { get; private set; }

    /// <summary>
    /// <paramref name="definition"/>을 실행할 준비를 합니다.
    /// </summary>
    /// <param name="definition">실행할 시퀀스 정의.</param>
    /// <param name="eventBus">단계 전환마다 <see cref="SequenceStepChangedEvent"/>를 발행하고, <see cref="AlarmRaisedEvent"/>를 구독해 자동 안전정지를 거는 데 씁니다.</param>
    /// <param name="actions">
    /// <see cref="SequenceStepDto.ActionType"/> → <see cref="ISequenceStepAction"/> 등록 목록. <c>null</c>이면
    /// 빈 목록(모든 단계가 <see cref="NoOpActionType"/> 취급 — 진입조건만으로 흐름을 검증하는 테스트에 유용).
    /// </param>
    /// <param name="triggerEvaluator"><c>null</c>이면 <see cref="SimpleSequenceTriggerEvaluator"/>(임시 구현, 클래스 XML 문서 참고).</param>
    /// <param name="resolveVariable">진입조건 식 안의 식별자(주로 태그 Id)를 값으로 바꿔주는 콜백. <c>null</c>이면 항상 <c>null</c> 반환.</param>
    /// <param name="pollIntervalMs">진입조건이 거짓일 때 재확인 간격(밀리초). 최소 1로 보정됩니다.</param>
    /// <exception cref="ArgumentException"><paramref name="definition"/>.Steps에 같은 Name이 2개 이상일 때.</exception>
    public SequenceExecutor(
        SequenceDefinition definition,
        IEventBus eventBus,
        IReadOnlyDictionary<string, ISequenceStepAction>? actions = null,
        ISequenceTriggerEvaluator? triggerEvaluator = null,
        Func<string, object?>? resolveVariable = null,
        int pollIntervalMs = 20)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _actions = actions ?? new Dictionary<string, ISequenceStepAction>();
        _triggerEvaluator = triggerEvaluator ?? new SimpleSequenceTriggerEvaluator();
        _resolveVariable = resolveVariable ?? (_ => null);
        _pollIntervalMs = Math.Max(1, pollIntervalMs);

        _orderedSteps = definition.Steps.OrderBy(s => s.Order).ToList();
        _stepsByName = new Dictionary<string, SequenceStepDto>();
        foreach (var step in _orderedSteps)
        {
            if (!_stepsByName.TryAdd(step.Name, step))
            {
                throw new ArgumentException($"시퀀스 '{definition.Id}'에 이름이 중복된 단계가 있습니다: '{step.Name}'", nameof(definition));
            }
        }

        _alarmSubscription = _eventBus.Subscribe<AlarmRaisedEvent>(OnAlarmRaised);
    }

    /// <summary>
    /// 첫 단계(<see cref="SequenceStepDto.Order"/> 오름차순)부터 순서대로 실행합니다. 이미 <see cref="SequenceState.Running"/>인
    /// 상태에서 다시 호출하면 <see cref="InvalidOperationException"/>을 던집니다(동시 실행 방지).
    /// </summary>
    /// <returns>최종 상태 — 모든 단계가 성공하면 <see cref="SequenceState.Completed"/>, 그 외(실패/타임아웃 분기 없음, 안전정지)는 <see cref="SequenceState.Faulted"/>.</returns>
    public async Task<SequenceState> RunAsync(CancellationToken ct = default)
    {
        if (State == SequenceState.Running)
        {
            throw new InvalidOperationException($"시퀀스 '{_definition.Id}'는 이미 실행 중입니다.");
        }

        if (_orderedSteps.Count == 0)
        {
            State = SequenceState.Completed;
            return State;
        }

        _abortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        State = SequenceState.Running;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        SequenceStepDto? current = _orderedSteps[0];
        while (current is not null)
        {
            try
            {
                await WaitWhilePausedAsync(_abortCts.Token);
            }
            catch (OperationCanceledException)
            {
                State = SequenceState.Faulted;
                break;
            }

            CurrentStepName = current.Name;
            PublishStepChanged(stopwatch.ElapsedMilliseconds);

            var outcome = await RunStepAsync(current, _abortCts.Token);
            switch (outcome)
            {
                case StepOutcome.Success:
                    current = NextStep(current);
                    break;

                case StepOutcome.Failed:
                    current = ResolveBranch(current.OnFailStepId);
                    if (current is null)
                    {
                        State = SequenceState.Faulted;
                    }

                    break;

                case StepOutcome.TimedOut:
                    current = ResolveBranch(current.OnTimeoutStepId);
                    if (current is null)
                    {
                        State = SequenceState.Faulted;
                    }

                    break;

                case StepOutcome.Aborted:
                default:
                    State = SequenceState.Faulted;
                    current = null;
                    break;
            }
        }

        if (State == SequenceState.Running)
        {
            State = SequenceState.Completed;
        }

        PublishStepChanged(stopwatch.ElapsedMilliseconds);
        return State;
    }

    /// <summary>진행 중인 시퀀스를 즉시 중단시킵니다(<see cref="SequenceState.Faulted"/>로 종료) — 사용자 호출, 또는 감시 대상 태그의 HH 알람 자동 안전정지에 씁니다.</summary>
    public void Abort() => _abortCts?.Cancel();

    /// <summary><see cref="SequenceState.Running"/>이면 <see cref="SequenceState.Paused"/>로 전환합니다(다음 단계 진입 직전에 반영 — 클래스 XML 문서 "일시정지" 항목 참고).</summary>
    public void Pause()
    {
        if (State == SequenceState.Running)
        {
            State = SequenceState.Paused;
        }
    }

    /// <summary><see cref="SequenceState.Paused"/>면 <see cref="SequenceState.Running"/>으로 되돌립니다.</summary>
    public void Resume()
    {
        if (State == SequenceState.Paused)
        {
            State = SequenceState.Running;
        }
    }

    /// <summary><see cref="AlarmRaisedEvent"/> 구독을 해제합니다(<see cref="IEventBus"/> XML 문서의 "반드시 Dispose" 규칙 준수).</summary>
    public void Dispose() => _alarmSubscription?.Dispose();

    private void OnAlarmRaised(AlarmRaisedEvent e)
    {
        if (State is SequenceState.Running or SequenceState.Paused
            && e.Level == AlarmLevel.HH
            && _definition.WatchedTagIds.Contains(e.TagId))
        {
            Abort();
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (State == SequenceState.Paused)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(_pollIntervalMs, ct);
        }
    }

    private async Task<StepOutcome> RunStepAsync(SequenceStepDto step, CancellationToken abortToken)
    {
        using var timeoutCts = step.TimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(abortToken)
            : null;
        timeoutCts?.CancelAfter(step.TimeoutMs);
        var stepToken = timeoutCts?.Token ?? abortToken;

        try
        {
            await WaitForTriggerAsync(step, stepToken);
            var success = await ExecuteActionAsync(step, stepToken);
            return success ? StepOutcome.Success : StepOutcome.Failed;
        }
        catch (OperationCanceledException) when (abortToken.IsCancellationRequested)
        {
            return StepOutcome.Aborted;
        }
        catch (OperationCanceledException)
        {
            return StepOutcome.TimedOut;
        }
        catch (Exception)
        {
            return StepOutcome.Failed;
        }
    }

    private async Task WaitForTriggerAsync(SequenceStepDto step, CancellationToken ct)
    {
        while (!_triggerEvaluator.Evaluate(step.TriggerExpression, _resolveVariable))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(_pollIntervalMs, ct);
        }
    }

    private async Task<bool> ExecuteActionAsync(SequenceStepDto step, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(step.ActionType) || step.ActionType == NoOpActionType)
        {
            return true;
        }

        if (!_actions.TryGetValue(step.ActionType, out var action))
        {
            throw new InvalidOperationException($"ActionType '{step.ActionType}'에 등록된 {nameof(ISequenceStepAction)}이 없습니다.");
        }

        return await action.ExecuteAsync(step, ct);
    }

    private SequenceStepDto? NextStep(SequenceStepDto current)
    {
        var idx = _orderedSteps.IndexOf(current);
        return idx >= 0 && idx + 1 < _orderedSteps.Count ? _orderedSteps[idx + 1] : null;
    }

    private SequenceStepDto? ResolveBranch(string? targetStepName) =>
        targetStepName is not null && _stepsByName.TryGetValue(targetStepName, out var step) ? step : null;

    private void PublishStepChanged(long elapsedMs)
    {
        if (CurrentStepName is not null)
        {
            _eventBus.Publish(new SequenceStepChangedEvent(_definition.Id, CurrentStepName, State, elapsedMs));
        }
    }
}
