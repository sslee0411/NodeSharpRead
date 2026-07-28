using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SequenceDefinition"/>/<see cref="SequenceStepDto"/>(CT-03b, 02번 문서 11번 탭 카드 5) 단위 테스트.
/// 완료 기준: TriggerExpression·TimeoutMs·OnFailStepId·OnTimeoutStepId가 SQ-01 설계와 1:1 대응하는지 확인.
/// </summary>
public class SequenceDefinitionTests
{
    private static SequenceStepDto CreateStep(string name, int order,
        string? onFailStepId = null, string? onTimeoutStepId = null, int timeoutMs = 0) =>
        new(Order: order, Name: name, TriggerExpression: "true",
            ActionType: "PlcWriteStep", ActionParams: new Dictionary<string, object?>(),
            TimeoutMs: timeoutMs, OnFailStepId: onFailStepId, OnTimeoutStepId: onTimeoutStepId);

    [Fact]
    public void TimeoutMs와_OnFail_OnTimeout_기본값은_각각_0과_null이다()
    {
        var step = new SequenceStepDto(0, "준비 확인", "true", "PlcReadStep", new Dictionary<string, object?>());

        Assert.Equal(0, step.TimeoutMs);
        Assert.Null(step.OnFailStepId);
        Assert.Null(step.OnTimeoutStepId);
    }

    [Fact]
    public void 진입조건_타임아웃_실패시이동_타임아웃시이동_필드가_각각_독립적으로_설정된다()
    {
        // SQ-01 SequenceExecutor 설계: TriggerExpression 평가 → 실패 시 OnFailStepId,
        // TimeoutMs 초과 시 OnTimeoutStepId로 분기 — 두 분기가 서로 다른 대상 Step을 가리킬 수 있어야 한다.
        var step = new SequenceStepDto(
            Order: 1, Name: "밸브 개방",
            TriggerExpression: "prevStep.Done && tag.Ready==true",
            ActionType: "PlcWriteStep",
            ActionParams: new Dictionary<string, object?> { ["tagId"] = "tag-1", ["value"] = true },
            TimeoutMs: 5000,
            OnFailStepId: "step-retry",
            OnTimeoutStepId: "step-safe-stop");

        Assert.Equal("prevStep.Done && tag.Ready==true", step.TriggerExpression);
        Assert.Equal(5000, step.TimeoutMs);
        Assert.Equal("step-retry", step.OnFailStepId);
        Assert.Equal("step-safe-stop", step.OnTimeoutStepId);
        Assert.NotEqual(step.OnFailStepId, step.OnTimeoutStepId); // 실패와 타임아웃이 서로 다른 단계로 분기 가능함을 확인
    }

    [Fact]
    public void SequenceDefinition은_Steps와_WatchedTagIds를_순서대로_보관한다()
    {
        var steps = new List<SequenceStepDto>
        {
            CreateStep("준비 확인", 0, onTimeoutStepId: "step-safe-stop"),
            CreateStep("밸브 개방", 1, onFailStepId: "step-retry", timeoutMs: 3000),
            CreateStep("안전 정지", 2),
        };
        var watchedTagIds = new List<string> { "tag-1", "tag-2" };

        var seq = new SequenceDefinition("seq-1", "1호기 펌프 기동 절차", steps, watchedTagIds);

        Assert.Equal("seq-1", seq.Id);
        Assert.Equal("1호기 펌프 기동 절차", seq.Name);
        Assert.Equal(3, seq.Steps.Count);
        Assert.Equal("준비 확인", seq.Steps[0].Name);
        Assert.Equal("밸브 개방", seq.Steps[1].Name);
        Assert.Equal("안전 정지", seq.Steps[2].Name);
        Assert.Equal(2, seq.WatchedTagIds.Count);
        Assert.Contains("tag-1", seq.WatchedTagIds);
    }

    [Fact]
    public void 서로_다른_Id로_두_시퀀스를_구분할_수_있다()
    {
        var seq1 = new SequenceDefinition("seq-1", "기동 절차", new List<SequenceStepDto>(), new List<string>());
        var seq2 = new SequenceDefinition("seq-2", "정지 절차", new List<SequenceStepDto>(), new List<string>());

        Assert.NotEqual(seq1.Id, seq2.Id);
        Assert.NotEqual(seq1, seq2);
    }
}
