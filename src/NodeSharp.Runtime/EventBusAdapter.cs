using NodeSharp.Contracts.Interfaces;
using NodeSharp.Util.Messaging;

namespace NodeSharp.Runtime;

/// <summary>
/// <see cref="IEventBus"/>(Contracts 계약)를 <see cref="EventBus"/>(NodeSharp.Util로 포팅된 lssLib.Messaging
/// 구현체)로 그대로 위임하는 어댑터입니다. Contracts는 구체 타입을 몰라야 하므로(<see cref="IEventBus"/>
/// XML 주석 참고 — Contracts→Runtime 순환 참조 방지), Runtime이 이 어댑터로 그 둘을 이어줍니다 —
/// <c>NodeExecutionGate</c>가 <c>SemaphoreSlim</c>을 감싸는 것과 같은 역할입니다.
/// 설계 근거: 02번 문서 3번 탭 카드5(<c>IEventBus</c> 계약), dev-csharp 스킬 lssLib.Messaging 문서(원본
/// <c>EventBus</c> 동작).
/// </summary>
/// <remarks>
/// 실제 이벤트 저장·발행 로직은 전부 <see cref="EventBus"/>에 있습니다 — 이 어댑터는 메서드 호출을
/// 그대로 전달만 할 뿐 별도 상태를 갖지 않습니다. 기본 생성자는 <see cref="EventBus.Instance"/>(앱
/// 전체 공유 싱글턴)를 감싸지만, 테스트에서는 매번 새 <see cref="EventBus"/> 인스턴스를 주입해 테스트
/// 간 구독이 서로 섞이지 않게 할 수 있습니다.
/// </remarks>
/// <example>
/// <code>
/// var adapter = new EventBusAdapter();   // EventBus.Instance를 감쌈
/// IEventBus bus = adapter;               // Contracts 계약으로 사용
///
/// var sub = bus.Subscribe&lt;NodeStartedEvent&gt;(e => Console.WriteLine($"{e.NodeId} 시작"));
/// bus.Publish(new NodeStartedEvent("n1"));
/// sub.Dispose();   // 반드시 해제
/// </code>
/// </example>
public sealed class EventBusAdapter : IEventBus
{
    private readonly EventBus _inner;

    /// <summary>앱 전체가 공유하는 <see cref="EventBus.Instance"/>를 감싸는 어댑터를 만듭니다.</summary>
    public EventBusAdapter() : this(EventBus.Instance) { }

    /// <summary>
    /// 특정 <see cref="EventBus"/> 인스턴스를 감싸는 어댑터를 만듭니다. 테스트에서 싱글턴 대신 독립된
    /// 인스턴스를 넣어, 여러 테스트가 같은 구독 목록을 공유하지 않게 할 때 사용합니다.
    /// </summary>
    public EventBusAdapter(EventBus inner) => _inner = inner;

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) => _inner.Subscribe(handler);

    /// <inheritdoc/>
    public void Publish<TEvent>(TEvent evt) => _inner.Publish(evt);
}
