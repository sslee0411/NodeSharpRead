using System.Collections.Concurrent;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

// 한글명: 메모리 기반 컨텍스트 저장소
/// <summary>
/// <see cref="IContextStore"/>의 기본 구현입니다 — 값을 프로세스 메모리에만 들고 있고, Runner를 다시
/// 시작하면 전부 사라집니다(Node-RED의 기본 memory Context Storage와 동일한 성격). <c>(scope, scopeId,
/// key)</c> 세 값을 하나의 키로 묶어 <see cref="ConcurrentDictionary{TKey,TValue}"/>에 저장하므로, 여러
/// 노드가 동시에 읽고 써도 개별 연산 자체는 안전합니다.
/// 설계 근거: 02번 문서 6번 탭 카드 1.
/// </summary>
/// <example>
/// <code>
/// var store = new InMemoryContextStore();
/// store.Set("node", "n1", "lastValue", 3.14);
/// double? v = store.Get&lt;double&gt;("node", "n1", "lastValue");   // 3.14
/// </code>
/// </example>
public sealed class InMemoryContextStore : IContextStore
{
    private readonly ConcurrentDictionary<(string Scope, string ScopeId, string Key), object?> _data = new();

    /// <inheritdoc/>
    public T? Get<T>(string scope, string scopeId, string key) =>
        _data.TryGetValue((scope, scopeId, key), out var value) && value is T typed ? typed : default;

    /// <inheritdoc/>
    public void Set(string scope, string scopeId, string key, object? value) =>
        _data[(scope, scopeId, key)] = value;

    /// <inheritdoc/>
    public IEnumerable<string> Keys(string scope, string scopeId) =>
        _data.Keys.Where(k => k.Scope == scope && k.ScopeId == scopeId).Select(k => k.Key).ToList();
}
