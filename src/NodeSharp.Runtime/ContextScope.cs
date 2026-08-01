using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

/// <summary>
/// <see cref="IContextStore"/>의 특정 <c>scope</c>/<c>scopeId</c> 하나만 바라보는 좁은 창(view)입니다.
/// 노드 코드가 매번 <c>store.Get&lt;T&gt;("flow", flowId, key)</c>처럼 scope/scopeId를 반복해서 넘기지
/// 않아도, <c>ctx.Flow.Get&lt;T&gt;(key)</c>처럼 짧게 쓸 수 있게 해줍니다(Node-RED의 <c>flow.get(key)</c>·
/// <c>global.get(key)</c>와 같은 사용감). 이 구조체 자체는 scope 이름을 하드코딩하지 않으므로,
/// <c>NodeContext</c>(<c>RT-09b</c> 이후, 06번 탭 카드1 정식 통합판)가 <c>Local</c>(node)/<c>Flow</c>/
/// <c>Global</c>/<c>Env</c> 4개를 각각 다른 scope 이름으로 만들어 씁니다 — 이 Step(<c>RT-09a</c>)에서는
/// <c>Flow</c>/<c>Global</c> 2단계만 직접 검증합니다(<c>Local</c>은 <c>RT-09b</c> 범위).
/// 설계 근거: 02번 문서 6번 탭 카드 1.
/// </summary>
/// <example>
/// <code>
/// var store = new InMemoryContextStore();
/// var flowScope = new ContextScope(store, "flow", "f1");
/// var globalScope = new ContextScope(store, "global", "");
///
/// flowScope.Set("counter", 1);
/// globalScope.Set("counter", 100);   // 같은 key "counter"지만 scope가 달라 서로 섞이지 않음
///
/// int? flowCounter = flowScope.Get&lt;int&gt;("counter");     // 1
/// int? globalCounter = globalScope.Get&lt;int&gt;("counter"); // 100
/// </code>
/// </example>
public readonly struct ContextScope
{
    private readonly IContextStore _store;
    private readonly string _scope;
    private readonly string _scopeId;

    /// <summary><paramref name="store"/> 안에서 <paramref name="scope"/>/<paramref name="scopeId"/> 하나만 바라보는 창을 만듭니다.</summary>
    public ContextScope(IContextStore store, string scope, string scopeId)
    {
        _store = store;
        _scope = scope;
        _scopeId = scopeId;
    }

    /// <summary>이 스코프 안에서 <paramref name="key"/> 값을 읽습니다. 값이 없으면 <c>default(T)</c>를 반환합니다.</summary>
    public T? Get<T>(string key) => _store.Get<T>(_scope, _scopeId, key);

    /// <summary>이 스코프 안에 <paramref name="key"/> 값을 저장(또는 덮어쓰기)합니다.</summary>
    public void Set(string key, object? value) => _store.Set(_scope, _scopeId, key, value);

    /// <summary>이 스코프 안에 저장된 모든 키 이름을 열거합니다.</summary>
    public IEnumerable<string> Keys() => _store.Keys(_scope, _scopeId);
}
