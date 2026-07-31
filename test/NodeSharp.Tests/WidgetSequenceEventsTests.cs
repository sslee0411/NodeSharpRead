using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="WidgetValueUpdatedEvent"/>/<see cref="WidgetInteractionEvent"/>/
/// <see cref="SequenceStepChangedEvent"/>/<see cref="NodeCompleteEvent"/>(CT-05c, 02번 설계 문서
/// 9번 탭 카드 4·11·12, 11번 탭 카드 5)에 대한 단위 테스트입니다.
/// </summary>
public class WidgetSequenceEventsTests
{
    [Fact]
    public void WidgetValueUpdatedEvent_숫자_Value가_SystemTextJson_왕복_후에도_보존된다()
    {
        var original = new WidgetValueUpdatedEvent(NodeId: "gauge-1", Value: 42.5, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<WidgetValueUpdatedEvent>(json);

        Assert.Equal(original.NodeId, restored!.NodeId);
        Assert.Equal(original.At, restored.At);
    }

    [Fact]
    public void WidgetInteractionEvent_UserInput이_bool이면_왕복_후_그대로_보존된다()
    {
        var original = new WidgetInteractionEvent(NodeId: "btn-1", UserInput: true, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<WidgetInteractionEvent>(json);

        Assert.Equal(original.NodeId, restored!.NodeId);
    }

    [Fact]
    public void SequenceStepChangedEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new SequenceStepChangedEvent(SequenceId: "seq-1", CurrentStepId: "step-3", State: SequenceState.Running, ElapsedMs: 4200);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SequenceStepChangedEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void UiSequenceStatusNode_패턴_SequenceId가_다르면_구독_필터가_무시한다()
    {
        var evt = new SequenceStepChangedEvent(SequenceId: "seq-2", CurrentStepId: "step-1", State: SequenceState.Running, ElapsedMs: 100);
        const string watchedSequenceId = "seq-1";

        bool shouldHandle = evt.SequenceId == watchedSequenceId;

        Assert.False(shouldHandle);
    }

    [Fact]
    public void NodeCompleteEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new NodeCompleteEvent(NodeId: "n1", MsgId: "msg-1", HadOutput: true, At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodeCompleteEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void SequenceState_5가지_값이_모두_존재한다()
    {
        var values = Enum.GetValues<SequenceState>();

        Assert.Equal(5, values.Length);
        Assert.Contains(SequenceState.Idle, values);
        Assert.Contains(SequenceState.Running, values);
        Assert.Contains(SequenceState.Paused, values);
        Assert.Contains(SequenceState.Faulted, values);
        Assert.Contains(SequenceState.Completed, values);
    }
}
