using System.Text.Json;
using NodeSharp.Contracts.Events;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="NodeStatusEvent"/>/<see cref="FlowActivityEvent"/>/<see cref="DebugMessageEvent"/>/
/// <see cref="NodeErrorEvent"/>(CT-05a, 02번 설계 문서 3번 탭 카드 7)에 대한 단위 테스트입니다. 완료
/// 기준이 요구하는 "LK-02 SignalR Hub가 그대로 직렬화해 전송 가능"은 LK-02가 아직 없어 지금은
/// System.Text.Json 왕복으로 대신 검증합니다(SignalR 기본 프로토콜과 동일한 직렬화기).
/// </summary>
public class MonitoringEventsTests
{
    [Fact]
    public void NodeStatusEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new NodeStatusEvent(NodeId: "n1", Fill: "green", Shape: "dot", Text: "연결됨", At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodeStatusEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void FlowActivityEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new FlowActivityEvent(FromNodeId: "n1", OutputPort: 0, ToNodeId: "n2", MsgId: "msg-1", At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FlowActivityEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void DebugMessageEvent_SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new DebugMessageEvent(NodeId: "n3", NodeName: "디버그", MsgJson: "{\"payload\":42}", At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DebugMessageEvent>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void NodeErrorEvent_StackTrace가_null이어도_왕복_시_보존된다()
    {
        // (LK-04) NodeErrorEvent가 노드 정보·예외 타입·msg 스냅샷까지 담도록 확장되었습니다
        // (FlowEngine.DispatchOneAsync, 03번 Step맵 LK-04). 필드가 늘어도 System.Text.Json
        // 왕복 검증 방식 자체는 그대로 유효합니다.
        var original = new NodeErrorEvent(
            NodeId: "n1",
            NodeName: "기동노드",
            NodeType: "function",
            ExceptionType: "InvalidOperationException",
            Message: "기동 실패",
            StackTrace: null,
            MsgId: "msg-1",
            MsgSnapshotJson: "{\"payload\":null}",
            At: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodeErrorEvent>(json);

        Assert.Equal(original, restored);
        Assert.Null(restored!.StackTrace);
    }
}
