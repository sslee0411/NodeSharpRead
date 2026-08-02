namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 이벤트 버스 계약
/// 역활 및 기능 : 노드가 이벤트를 구독·발행할 때 쓰는 최소 계약
///
/// 노드가 이벤트를 구독·발행할 때 쓰는 최소 계약입니다. 실제 구현체(<c>NodeSharp.Util.Messaging.EventBus</c>를
/// 감싸는 어댑터)는 <c>NodeSharp.Runtime</c> 소속이며, Contracts는 이 인터페이스만 알면 되므로 Runtime을
/// 참조하지 않아도 됩니다(<see cref="INodeContext"/>·<see cref="IStructureService"/>·<see cref="IScheduler"/>와
/// 같은 이유 — 구체 타입 대신 인터페이스에 의존해야 Contracts→Runtime 순환 참조가 생기지 않습니다).
/// 설계 근거: 02번 문서 3번 탭 카드5("이벤트 구독/해제 규칙").
/// </summary>
/// <remarks>
/// <see cref="Subscribe{TEvent}"/>가 돌려주는 <see cref="IDisposable"/>은 반드시 필드에 저장해뒀다가,
/// 노드가 <c>OnCloseAsync</c>로 끝날 때 <c>Dispose()</c>해야 합니다. 이걸 빼먹으면 재배포할 때마다 오래된
/// 구독이 계속 쌓여 같은 이벤트를 여러 번 처리하거나 메모리가 계속 늘어납니다(02번 문서 3번 탭 카드5에
/// 기록된 실제 문제). 지금은 완료 기준(RT-07)에 필요한 최소 멤버(Subscribe/Publish)만 있고, 필요해지는
/// 시점마다(예: 비동기 발행) 멤버를 추가할 예정입니다 — <see cref="INodeContext"/>와 같은 점진적 확장
/// 원칙입니다.
/// </remarks>
/// <example>
/// <code>
/// // 노드 안에서 사용하는 모습(개념 예시 — 실제 ctx.EventBus 연결은 RT-09 NodeContext에서)
/// private IDisposable? _subscription;
///
/// public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
/// {
///     _subscription = eventBus.Subscribe&lt;NodeStatusEvent&gt;(e =&gt; UpdateUi(e));
///     return Task.CompletedTask;
/// }
///
/// public Task OnCloseAsync(INodeContext ctx)
/// {
///     _subscription?.Dispose();   // 반드시 해제 — 위 remarks 참고
///     return Task.CompletedTask;
/// }
/// </code>
/// </example>
public interface IEventBus
{
    /// <summary>
    /// <typeparamref name="TEvent"/> 타입 이벤트가 발행될 때마다 <paramref name="handler"/>를 호출하도록
    /// 구독합니다. 반환값을 버리지 마세요 — 구독을 끊으려면 이 값의 <c>Dispose()</c>를 호출해야 합니다.
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    /// <summary><paramref name="evt"/>를 이 타입을 구독 중인 모든 핸들러에게 전달합니다.</summary>
    void Publish<TEvent>(TEvent evt);
}
