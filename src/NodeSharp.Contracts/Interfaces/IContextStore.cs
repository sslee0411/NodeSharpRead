namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// 노드/Flow(탭)/전역 3단계 Context 변수를 저장·조회하는 계약입니다(Node-RED의 <c>context.get/set</c>,
/// <c>flow.get/set</c>, <c>global.get/set</c>에 대응). 값 하나는 <c>(scope, scopeId, key)</c> 세 가지로
/// 구분됩니다 — <c>scope</c>는 "node"/"flow"/"global" 같은 단계 이름, <c>scopeId</c>는 그 단계 안에서 어느
/// 대상인지(노드 Id, Flow Id, 전역은 빈 문자열), <c>key</c>는 실제 변수 이름입니다. 구현체는
/// <c>InMemoryContextStore</c>(NodeSharp.Runtime, 기본 메모리 구현)이며, 나중에 파일·DB 기반 구현으로
/// 바꿔 끼워도(<c>RT-09b</c>) 이 인터페이스를 쓰는 코드는 그대로 유지됩니다.
/// 설계 근거: 02번 문서 6번 탭(공유 서비스/Context) 카드 1.
/// </summary>
/// <remarks>
/// 같은 <c>key</c>라도 <c>scope</c>나 <c>scopeId</c>가 다르면 완전히 별개의 값으로 취급됩니다 — 예를 들어
/// 노드 A의 <c>node</c> 스코프에 있는 <c>"count"</c>와 <c>flow</c> 스코프에 있는 <c>"count"</c>는 서로 다른
/// 값을 가질 수 있습니다. 이 "섞이지 않는다"는 성질이 <c>RT-09a</c>의 완료 기준입니다.
/// </remarks>
/// <example>
/// <code>
/// IContextStore store = new InMemoryContextStore();
///
/// // 1) 전역(global) 스코프 — scopeId는 항상 빈 문자열
/// store.Set("global", "", "totalCount", 42);
/// int? total = store.Get&lt;int&gt;("global", "", "totalCount");   // 42
///
/// // 2) Flow(탭) 스코프 — 같은 key라도 scopeId(Flow Id)가 다르면 별개
/// store.Set("flow", "f1", "counter", 1);
/// store.Set("flow", "f2", "counter", 100);
/// int? c1 = store.Get&lt;int&gt;("flow", "f1", "counter");   // 1 — f2의 값과 섞이지 않음
///
/// // 3) 존재하지 않는 키를 읽으면 default(T) 반환(예외 없음)
/// int? missing = store.Get&lt;int&gt;("global", "", "no-such-key");   // 0(int의 default)
///
/// // 4) 특정 scope+scopeId 안의 모든 키 이름 열거
/// IEnumerable&lt;string&gt; keys = store.Keys("flow", "f1");   // ["counter"]
/// </code>
/// </example>
public interface IContextStore
{
    /// <summary>
    /// <paramref name="scope"/>/<paramref name="scopeId"/>/<paramref name="key"/>로 저장된 값을 <typeparamref name="T"/>
    /// 타입으로 읽습니다. 값이 없거나 저장된 값의 실제 타입이 <typeparamref name="T"/>가 아니면
    /// <c>default(T)</c>를 반환합니다(예외를 던지지 않음).
    /// </summary>
    T? Get<T>(string scope, string scopeId, string key);

    /// <summary><paramref name="scope"/>/<paramref name="scopeId"/>/<paramref name="key"/>에 값을 저장(또는 덮어쓰기)합니다.</summary>
    void Set(string scope, string scopeId, string key, object? value);

    /// <summary><paramref name="scope"/>/<paramref name="scopeId"/> 안에 저장된 모든 키 이름을 열거합니다. 값이 하나도 없으면 빈 목록을 반환합니다.</summary>
    IEnumerable<string> Keys(string scope, string scopeId);
}
