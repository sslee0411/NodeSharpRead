namespace NodeSharp.Util.Messaging;

// 한글명: 이벤트 버스
/// <summary>
/// 타입별로 이벤트를 구독·발행하는 Pub/Sub(발행-구독) 허브입니다. lssLib.Messaging.EventBus 원본을
/// 구조·이름 그대로 포팅(복사)했습니다 — <c>D:\lssLib</c>를 직접 참조(ProjectReference)하지 않고,
/// 같은 동작을 하는 코드를 NodeSharp.Util로 옮겨왔습니다(포팅 정책, LL-00). 앱 전체에서 하나만 쓰면
/// 되므로 <see cref="Instance"/>로 접근하는 싱글턴입니다.
/// 설계 근거: dev-csharp 스킬 lssLib.Messaging 문서, 02번 구조설계 문서 3번 탭 카드5(구독 해제 규칙).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>구독은 반드시 해제해야 함</b>: <see cref="Subscribe{TEvent}(Action{TEvent})"/>가 돌려주는
/// <see cref="IDisposable"/>을 버리지 말고 필드에 저장해뒀다가, 더 이상 필요 없어지면(예: 노드가
/// <c>OnCloseAsync</c>로 종료될 때) 반드시 <c>Dispose()</c>를 호출해야 합니다. 그렇지 않으면 오래된
/// 구독이 계속 쌓여 같은 이벤트가 여러 번 처리되거나 메모리가 계속 늘어납니다(02번 문서 3번 탭 카드5,
/// "이벤트 구독/해제 규칙").</item>
/// <item><b>Publish(동기)는 막힐 수 있음</b>: 구독한 핸들러가 비동기(Task를 반환)인데
/// <see cref="Publish{TEvent}"/>(동기 발행)로 부르면, 그 핸들러가 끝날 때까지 현재 스레드가 기다리게
/// 됩니다. UI 스레드에서는 이 대기가 화면이 멈추는 원인이 될 수 있으므로, 비동기 핸들러가 있을 수
/// 있는 상황이라면 <see cref="PublishAsync{TEvent}"/>를 쓰는 것이 안전합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 이벤트 타입 선언(EventMessage 상속)
/// public record SensorDataEvent(int DeviceId, float Temperature) : EventMessage;
///
/// // 2) 구독(람다) — 반환된 IDisposable을 반드시 보관
/// IDisposable sub = EventBus.Instance.Subscribe&lt;SensorDataEvent&gt;(e =&gt;
///     Console.WriteLine($"온도: {e.Temperature}°C"));
///
/// // 3) 발행 — 여러 핸들러가 완료될 때까지 기다리고 싶으면 PublishAsync 권장
/// await EventBus.Instance.PublishAsync(new SensorDataEvent(DeviceId: 1, Temperature: 42.5f));
///
/// // 4) 더 이상 필요 없으면 반드시 해제(그렇지 않으면 계속 이벤트를 받음)
/// sub.Dispose();
/// </code>
/// </example>
public sealed class EventBus
{
    private static readonly Lazy<EventBus> _instance = new(() => new EventBus());

    /// <summary>앱 전체에서 공유하는 단일 인스턴스입니다.</summary>
    public static EventBus Instance => _instance.Value;

    // 이벤트 타입(Type)별로 구독 목록을 보관합니다. 목록 안의 각 항목은 "구독 식별자 + 실제 처리 함수"
    // 쌍입니다 — 식별자는 나중에 구독을 해제할 때 어떤 항목을 지울지 찾는 용도입니다.
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<(Guid Id, Func<object, Task> Invoke)>> _subscriptions = new();

    /// <summary>
    /// 새 <see cref="EventBus"/> 인스턴스를 만듭니다. 앱 코드는 보통 이 생성자 대신
    /// <see cref="Instance"/>(앱 전체 공유 싱글턴)를 씁니다. 이 생성자를 public으로 열어둔 이유는
    /// 테스트에서 서로 독립된 <see cref="EventBus"/>를 여러 개 만들어, 각 테스트의 구독 목록이 다른
    /// 테스트나 <see cref="Instance"/>와 섞이지 않게 하기 위해서입니다(<c>NodeSharp.Runtime.EventBusAdapter</c>도
    /// 이 생성자로 특정 인스턴스를 감쌀 수 있습니다 — 해당 클래스의 XML 주석 참고).
    /// </summary>
    public EventBus() { }

    /// <summary>
    /// <typeparamref name="TEvent"/> 타입 이벤트가 발행될 때마다 <paramref name="handler"/>(동기 람다)를
    /// 호출하도록 구독합니다. 반환된 <see cref="IDisposable"/>을 <c>Dispose()</c>하면 구독이 해제됩니다.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) =>
        SubscribeCore<TEvent>(e =>
        {
            handler(e);
            return Task.CompletedTask;
        });

    /// <summary>
    /// <typeparamref name="TEvent"/> 타입 이벤트가 발행될 때마다 <paramref name="handler"/>(비동기 람다)를
    /// 호출하도록 구독합니다. 이름이 <see cref="Subscribe{TEvent}(Action{TEvent})"/>와 다른 이유는 C#
    /// 컴파일러 때문입니다 — 만약 이름이 같으면, <c>async e =&gt; await ...</c>처럼 반환값이 없는 비동기
    /// 람다를 넘길 때 컴파일러가 이걸 <c>Action&lt;TEvent&gt;</c> 오버로드와 <c>Func&lt;TEvent, Task&gt;</c>
    /// 오버로드 중 어느 쪽에 맞출지 정하지 못해 "모호한 호출"(CS0121) 오류가 납니다. 이름을 다르게
    /// 지어서 애초에 그 상황이 생기지 않게 했습니다. <see cref="Publish{TEvent}"/>(동기 발행)와 함께
    /// 쓰면 발행 쪽 스레드가 이 핸들러가 끝날 때까지 기다리게 되니 주의하세요(위 remarks 참고).
    /// </summary>
    public IDisposable SubscribeAsync<TEvent>(Func<TEvent, Task> handler) =>
        SubscribeCore(handler);

    /// <summary>
    /// 람다 대신 <see cref="IEventHandler{TEvent}"/>를 구현한 클래스로 이벤트를 처리하고 싶을 때
    /// 사용하는 구독 오버로드입니다. 내부적으로는 <paramref name="handler"/>.<c>HandleAsync</c>를 호출하는
    /// 것과 동일합니다.
    /// </summary>
    public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler) =>
        SubscribeCore<TEvent>(handler.HandleAsync);

    private IDisposable SubscribeCore<TEvent>(Func<TEvent, Task> handler)
    {
        var type = typeof(TEvent);
        var id = Guid.NewGuid();

        // 실제 저장 형태는 object → Task 함수입니다. 구독할 때 캐스팅 코드를 한 번만 만들어두면,
        // 발행할 때는 이벤트 타입을 몰라도(Type 하나로) 저장된 함수를 그대로 호출할 수 있습니다.
        Task Wrapped(object obj) => handler((TEvent)obj);

        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(type, out var list))
            {
                list = new List<(Guid, Func<object, Task>)>();
                _subscriptions[type] = list;
            }

            list.Add((id, Wrapped));
        }

        return new Subscription(this, type, id);
    }

    /// <summary>구독 해제 — <see cref="Subscription"/>이 <c>Dispose()</c>될 때 호출합니다.</summary>
    private void Unsubscribe(Type type, Guid id)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(type, out var list))
            {
                list.RemoveAll(item => item.Id == id);
            }
        }
    }

    /// <summary>
    /// <paramref name="evt"/>를 구독 중인 모든 핸들러에게 즉시(동기적으로) 전달합니다. 구독자가 없으면
    /// 아무 일도 일어나지 않습니다(예외 없음). 핸들러 중 비동기인 것이 있다면 그 핸들러가 끝날 때까지
    /// 현재 스레드가 기다립니다 — UI 스레드 블로킹 위험은 위 remarks를 참고하세요.
    /// </summary>
    public void Publish<TEvent>(TEvent evt)
    {
        foreach (var invoke in GetHandlersSnapshot(typeof(TEvent)))
        {
            var task = invoke(evt);
            if (!task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// <paramref name="evt"/>를 구독 중인 모든 핸들러에게 전달하고, 그 핸들러들이 모두 끝날 때까지
    /// 비동기로 기다립니다(<c>Task.WhenAll</c>). UI 스레드를 막지 않으므로 <see cref="Publish{TEvent}"/>
    /// 보다 이 메서드를 우선 사용하는 것을 권장합니다.
    /// </summary>
    public Task PublishAsync<TEvent>(TEvent evt)
    {
        var handlers = GetHandlersSnapshot(typeof(TEvent));
        return Task.WhenAll(handlers.Select(invoke => invoke(evt)));
    }

    private List<Func<object, Task>> GetHandlersSnapshot(Type type)
    {
        lock (_gate)
        {
            // 발행 도중 다른 스레드가 구독/해제를 해도 영향받지 않도록, 현재 목록을 복사해서 반환합니다.
            return _subscriptions.TryGetValue(type, out var list)
                ? list.Select(item => item.Invoke).ToList()
                : new List<Func<object, Task>>();
        }
    }

    /// <summary>
    /// <see cref="Subscribe{TEvent}(Action{TEvent})"/> 등이 반환하는 구독 토큰입니다.
    /// <c>Dispose()</c>가 호출되면 자신을 만든 <see cref="EventBus"/>에서 해당 구독을 제거합니다.
    /// 두 번 이상 <c>Dispose()</c>를 호출해도 안전합니다(두 번째 호출부터는 아무 일도 하지 않음).
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly EventBus _bus;
        private readonly Type _type;
        private readonly Guid _id;
        private bool _disposed;

        public Subscription(EventBus bus, Type type, Guid id)
        {
            _bus = bus;
            _type = type;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bus.Unsubscribe(_type, _id);
        }
    }
}
