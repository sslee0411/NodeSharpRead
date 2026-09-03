using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SequenceExecutor"/>(SQ-01)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 SQ-01):
/// "3단계 이상을 순서대로 실행했을 때 순서·타임아웃·실패시이동 규칙대로 진행되는지 확인". 테스트마다
/// 새 <see cref="EventBus"/> 인스턴스를 감싸는 <see cref="EventBusAdapter"/>를 만들어 써서 테스트끼리
/// 구독이 섞이지 않게 합니다(EventBusTests.cs와 동일한 관례).
/// </summary>
public class SequenceExecutorTests
{
    private static IEventBus NewBus() => new EventBusAdapter(new EventBus());

    /// <summary>테스트 안에서 임의 시점에 성공/실패를 바꿔가며 반환할 수 있는 가짜 동작.</summary>
    private sealed class FakeAction : ISequenceStepAction
    {
        private readonly List<string> _calls;
        public bool Result { get; set; } = true;

        public FakeAction(List<string> calls) => _calls = calls;

        public Task<bool> ExecuteAsync(SequenceStepDto step, CancellationToken ct)
        {
            _calls.Add(step.Name);
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task 진입조건이_모두_true인_3단계_이상을_순서대로_실행하면_Completed로_끝난다()
    {
        var calls = new List<string>();
        var action = new FakeAction(calls);
        var def = new SequenceDefinition(
            "seq-1", "3단계 순서 확인",
            new[]
            {
                new SequenceStepDto(0, "1단계", "true", "Do", new Dictionary<string, object?>()),
                new SequenceStepDto(1, "2단계", "true", "Do", new Dictionary<string, object?>()),
                new SequenceStepDto(2, "3단계", "true", "Do", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), new Dictionary<string, ISequenceStepAction> { ["Do"] = action });

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
        Assert.Equal(new[] { "1단계", "2단계", "3단계" }, calls);
    }

    [Fact]
    public async Task Order가_뒤섞여_전달돼도_Order_오름차순으로_실행된다()
    {
        var calls = new List<string>();
        var action = new FakeAction(calls);
        var def = new SequenceDefinition(
            "seq-2", "Order 정렬 확인",
            new[]
            {
                new SequenceStepDto(2, "세번째", "true", "Do", new Dictionary<string, object?>()),
                new SequenceStepDto(0, "첫번째", "true", "Do", new Dictionary<string, object?>()),
                new SequenceStepDto(1, "두번째", "true", "Do", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), new Dictionary<string, ISequenceStepAction> { ["Do"] = action });

        await executor.RunAsync();

        Assert.Equal(new[] { "첫번째", "두번째", "세번째" }, calls);
    }

    [Fact]
    public async Task 동작이_실패하면_OnFailStepId로_분기한다()
    {
        var calls = new List<string>();
        var failing = new FakeAction(calls) { Result = false };
        var recovery = new FakeAction(calls);
        var def = new SequenceDefinition(
            "seq-3", "실패 분기 확인",
            new[]
            {
                new SequenceStepDto(0, "밸브 열기", "true", "Fail", new Dictionary<string, object?>(), OnFailStepId: "안전정지"),
                new SequenceStepDto(1, "펌프 기동", "true", "Ok", new Dictionary<string, object?>()),
                new SequenceStepDto(2, "안전정지", "true", "Ok", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var actions = new Dictionary<string, ISequenceStepAction> { ["Fail"] = failing, ["Ok"] = recovery };
        var executor = new SequenceExecutor(def, NewBus(), actions);

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
        Assert.Equal(new[] { "밸브 열기", "안전정지" }, calls);   // "펌프 기동"은 건너뜀
    }

    [Fact]
    public async Task 실패해도_OnFailStepId가_없으면_Faulted로_종료한다()
    {
        var calls = new List<string>();
        var failing = new FakeAction(calls) { Result = false };
        var def = new SequenceDefinition(
            "seq-4", "분기 없는 실패",
            new[] { new SequenceStepDto(0, "1단계", "true", "Fail", new Dictionary<string, object?>()) },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), new Dictionary<string, ISequenceStepAction> { ["Fail"] = failing });

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Faulted, final);
    }

    [Fact]
    public async Task 진입조건이_타임아웃_안에_true가_되지_않으면_OnTimeoutStepId로_분기한다()
    {
        var calls = new List<string>();
        var recovery = new FakeAction(calls);
        var def = new SequenceDefinition(
            "seq-5", "타임아웃 분기 확인",
            new[]
            {
                new SequenceStepDto(0, "대기", "false", "Ok", new Dictionary<string, object?>(), TimeoutMs: 60, OnTimeoutStepId: "안전정지"),
                new SequenceStepDto(1, "안전정지", "true", "Ok", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), new Dictionary<string, ISequenceStepAction> { ["Ok"] = recovery }, pollIntervalMs: 5);

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
        Assert.Equal(new[] { "안전정지" }, calls);   // "대기" 단계의 동작 자체는 트리거가 안 돼 한 번도 호출되지 않음
    }

    [Fact]
    public async Task 타임아웃이어도_OnTimeoutStepId가_없으면_Faulted로_종료한다()
    {
        var def = new SequenceDefinition(
            "seq-6", "분기 없는 타임아웃",
            new[] { new SequenceStepDto(0, "대기", "false", "NoOp", new Dictionary<string, object?>(), TimeoutMs: 40) },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), pollIntervalMs: 5);

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Faulted, final);
    }

    [Fact]
    public async Task ActionType이_비어있으면_등록된_동작_없이_바로_성공한다()
    {
        var def = new SequenceDefinition(
            "seq-7", "무동작 단계",
            new[] { new SequenceStepDto(0, "그냥 통과", "true", "", new Dictionary<string, object?>()) },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus());

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
    }

    [Fact]
    public async Task 단계_전환마다_SequenceStepChangedEvent가_발행된다()
    {
        var bus = NewBus();
        var received = new List<SequenceStepChangedEvent>();
        using var sub = bus.Subscribe<SequenceStepChangedEvent>(received.Add);
        var def = new SequenceDefinition(
            "seq-8", "이벤트 발행 확인",
            new[]
            {
                new SequenceStepDto(0, "1단계", "true", "", new Dictionary<string, object?>()),
                new SequenceStepDto(1, "2단계", "true", "", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, bus);

        await executor.RunAsync();

        Assert.Contains(received, e => e.SequenceId == "seq-8" && e.CurrentStepId == "1단계");
        Assert.Contains(received, e => e.SequenceId == "seq-8" && e.CurrentStepId == "2단계" && e.State == SequenceState.Completed);
    }

    [Fact]
    public async Task 감시_태그에_HH_알람이_발생하면_자동으로_Faulted로_종료된다()
    {
        var bus = NewBus();
        var def = new SequenceDefinition(
            "seq-9", "알람 자동 안전정지",
            new[] { new SequenceStepDto(0, "대기", "false", "", new Dictionary<string, object?>()) },   // 진입조건이 항상 거짓이라 계속 대기
            new[] { "tag-pressure" });
        var executor = new SequenceExecutor(def, bus, pollIntervalMs: 5);

        var runTask = executor.RunAsync();
        await Task.Delay(20);   // 대기 상태 진입 확인
        bus.Publish(new AlarmRaisedEvent("tag-pressure", AlarmLevel.HH, 99.0, DateTime.UtcNow));

        var final = await runTask;

        Assert.Equal(SequenceState.Faulted, final);
    }

    [Fact]
    public async Task 감시_대상이_아닌_태그의_HH_알람은_무시한다()
    {
        var bus = NewBus();
        var def = new SequenceDefinition(
            "seq-10", "감시 대상 외 알람 무시",
            new[] { new SequenceStepDto(0, "1단계", "true", "", new Dictionary<string, object?>()) },
            new[] { "tag-pressure" });
        var executor = new SequenceExecutor(def, bus);

        bus.Publish(new AlarmRaisedEvent("tag-other", AlarmLevel.HH, 1.0, DateTime.UtcNow));
        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
    }

    [Fact]
    public async Task Abort를_호출하면_Faulted로_종료한다()
    {
        var def = new SequenceDefinition(
            "seq-11", "수동 Abort",
            new[] { new SequenceStepDto(0, "대기", "false", "", new Dictionary<string, object?>()) },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), pollIntervalMs: 5);

        var runTask = executor.RunAsync();
        await Task.Delay(20);
        executor.Abort();

        Assert.Equal(SequenceState.Faulted, await runTask);
    }

    [Fact]
    public async Task 이미_실행_중이면_다시_RunAsync를_호출할_수_없다()
    {
        var def = new SequenceDefinition(
            "seq-12", "중복 실행 방지",
            new[] { new SequenceStepDto(0, "대기", "false", "", new Dictionary<string, object?>()) },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), pollIntervalMs: 5);

        var runTask = executor.RunAsync();
        await Task.Delay(10);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunAsync());

        executor.Abort();
        await runTask;
    }

    [Fact]
    public async Task ActionType이_등록되지_않은_이름이면_실패로_처리돼_OnFailStepId로_분기한다()
    {
        var calls = new List<string>();
        var recovery = new FakeAction(calls);
        var def = new SequenceDefinition(
            "seq-13", "미등록 ActionType",
            new[]
            {
                new SequenceStepDto(0, "1단계", "true", "없는동작", new Dictionary<string, object?>(), OnFailStepId: "복구"),
                new SequenceStepDto(1, "복구", "true", "Ok", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());
        var executor = new SequenceExecutor(def, NewBus(), new Dictionary<string, ISequenceStepAction> { ["Ok"] = recovery });

        var final = await executor.RunAsync();

        Assert.Equal(SequenceState.Completed, final);
        Assert.Equal(new[] { "복구" }, calls);
    }

    [Fact]
    public void 시퀀스_안에_이름이_중복된_단계가_있으면_생성자에서_예외를_던진다()
    {
        var def = new SequenceDefinition(
            "seq-14", "중복 이름",
            new[]
            {
                new SequenceStepDto(0, "같은이름", "true", "", new Dictionary<string, object?>()),
                new SequenceStepDto(1, "같은이름", "true", "", new Dictionary<string, object?>()),
            },
            Array.Empty<string>());

        Assert.Throws<ArgumentException>(() => new SequenceExecutor(def, NewBus()));
    }
}
