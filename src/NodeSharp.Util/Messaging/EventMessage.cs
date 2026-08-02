namespace NodeSharp.Util.Messaging;

// 한글명: 이벤트 메시지(기반 타입)
/// <summary>
/// 모든 이벤트 메시지가 상속받는 기반 타입입니다. lssLib.Messaging.EventBus 원본을 그대로
/// 포팅(복사)한 것입니다 — <c>D:\lssLib</c>를 직접 참조하지 않고 구조·이름만 똑같이 옮겨왔습니다.
/// 이벤트를 새로 만들 때는 이 타입을 상속하는 <c>record</c>로 정의하면(예: <c>record SensorDataEvent(int
/// DeviceId, float Temperature) : EventMessage;</c>) <see cref="Timestamp"/>가 자동으로 채워집니다.
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서.
/// </summary>
/// <example>
/// <code>
/// public record NodeStartedEvent(string NodeId) : EventMessage;
///
/// var evt = new NodeStartedEvent("n1");
/// DateTime when = evt.Timestamp;   // 생성 시각(UTC)이 자동으로 들어감
/// </code>
/// </example>
public abstract record EventMessage
{
    /// <summary>이 이벤트가 만들어진 시각(UTC)입니다. 생성자를 따로 호출하지 않아도 자동으로 채워집니다.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
