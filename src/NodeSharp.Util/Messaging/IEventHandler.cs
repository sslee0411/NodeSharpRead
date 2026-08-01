namespace NodeSharp.Util.Messaging;

/// <summary>
/// 람다 대신 별도 클래스로 이벤트를 처리하고 싶을 때 구현하는 인터페이스입니다. lssLib.Messaging.EventBus
/// 원본을 그대로 포팅(복사)했습니다 — 이벤트 하나에 처리 로직이 여러 줄이거나, 상태를 갖는 핸들러가
/// 필요할 때 람다 대신 이 방식을 씁니다.
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서.
/// </summary>
/// <example>
/// <code>
/// public class SensorAlertHandler : IEventHandler&lt;SensorDataEvent&gt;
/// {
///     public Task HandleAsync(SensorDataEvent e)
///     {
///         if (e.Temperature &gt; 80)
///             Console.WriteLine($"고온 경보: {e.Temperature}°C");
///         return Task.CompletedTask;
///     }
/// }
///
/// var sub = EventBus.Instance.Subscribe(new SensorAlertHandler());
/// </code>
/// </example>
/// <typeparam name="TEvent">처리할 이벤트 타입(보통 <see cref="EventMessage"/>를 상속한 record).</typeparam>
public interface IEventHandler<in TEvent>
{
    /// <summary>이 타입의 이벤트가 발행될 때마다 호출됩니다.</summary>
    Task HandleAsync(TEvent e);
}
