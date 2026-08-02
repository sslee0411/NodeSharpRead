using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

// 한글명: 파일 기반 컨텍스트 저장소
/// <summary>
/// <see cref="IContextStore"/>의 파일 기반 구현체입니다(RT-09c, 02번 문서 6번 탭 카드1 다이어그램의
/// "파일 — lssLib JsonWriteService" 플러그인 슬롯). 모든 값을 메모리 캐시에 갖고 있으면서(빠른 읽기)
/// <see cref="Set"/> 호출마다 JSON 파일 하나에 통째로 다시 써서(<c>Newtonsoft.Json</c>, 이미 프로젝트가
/// <see cref="global::NodeSharp.Contracts.Models.Msg"/>에서 쓰던 라이브러리 그대로) Runner를 재시작해도
/// 값이 남아있게 합니다(Node-RED의 <c>localfilesystem</c> Context Storage와 동일한 개념). <see cref="ContextScope"/>/
/// <c>NodeContext</c>는 <see cref="IContextStore"/> 인터페이스로만 값을 주고받으므로, 이 클래스로 교체해도
/// 그 두 타입은 코드 변경이 전혀 필요 없습니다(완료 기준, RT-09b <see cref="InMemoryContextStore"/>와
/// 동일한 자리에 그대로 대입 가능).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>스킬 문서에 "JsonWriteService"라는 타입 자체는 없음</b> — 02번 문서 다이어그램은 lssLib의
/// "JsonWriteService"를 언급하지만, dev-csharp 스킬 문서에는 그 이름의 타입이 없고 대신
/// <c>lssLib.Extensions.TextExtensions</c>의 <c>SaveJsonAsync</c>/<c>LoadJsonAsync</c>(단순 JSON 파일
/// 저장/로드 확장 메서드)가 가장 가까운 기능이었습니다. 그 확장 메서드 자체를 포팅하는 대신(아직
/// <c>lssLib.Extensions</c> 전체가 <c>NodeSharp.Util</c>로 포팅되지 않았고, 이 Step에 필요한 기능은
/// "객체 하나를 JSON 파일로 저장/로드"뿐이라 범위가 작음), 이미 <c>NodeSharp.Contracts</c>가 참조 중인
/// 동일한 <c>Newtonsoft.Json</c>으로 직접 구현했습니다(사용자 확인, 2026-08 세션 — "JSON 파일 저장소
/// (FileContextStore) 구현" 선택).</item>
/// <item><b>타입 보존은 프로세스 재시작 전까지만 보장</b> — 같은 인스턴스 안에서 <see cref="Set"/> 직후
/// <see cref="Get{T}"/>를 호출하면 원본 CLR 타입 그대로 돌려줍니다(메모리 캐시를 그대로 조회하므로
/// <see cref="InMemoryContextStore"/>와 동일). 하지만 프로세스를 재시작해 파일에서 다시 불러온 뒤에는
/// 값이 JSON을 거치며 <c>JToken</c>(<c>Newtonsoft.Json.Linq</c>)으로 남습니다 — <see cref="Get{T}"/>가
/// <c>JToken.ToObject&lt;T&gt;()</c>로 재변환을 시도하지만, 원본이 <c>ExpandoObject</c>처럼 JSON에 없는
/// C# 전용 타입이었다면 완벽히 복원되지 않을 수 있습니다(알려진 제약 — 실제 Node-RED
/// <c>localfilesystem</c> Context Storage도 같은 제약을 가짐).</item>
/// <item><b>매 <see cref="Set"/>마다 파일 전체를 다시 씀</b> — 변경분만 추가하는 대신 전체 스냅샷을
/// 통째로 다시 저장하는 가장 단순한 구현입니다("최소 먼저" 원칙, <c>CronExpression</c>의 `*`·쉼표 목록만
/// 지원하는 최소 파서와 동일한 선례) — 값이 아주 많거나 쓰기가 아주 잦은 환경에서는 느릴 수 있어
/// 향후 개선 여지로 남깁니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var store = new FileContextStore(@"C:\NodeSharp\context.json");
/// store.Set("global", "", "count", 42);          // 즉시 파일에도 저장됨
/// int count = store.Get&lt;int&gt;("global", "", "count");   // 42 (같은 인스턴스라 원본 타입 그대로)
///
/// // 프로세스 재시작 후(새 인스턴스로 같은 파일을 다시 읽음)
/// var reloaded = new FileContextStore(@"C:\NodeSharp\context.json");
/// int restored = reloaded.Get&lt;int&gt;("global", "", "count");   // 42 (JToken → int로 재변환)
/// </code>
/// </example>
public sealed class FileContextStore : IContextStore
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<(string Scope, string ScopeId, string Key), object?> _data = new();
    private readonly object _fileLock = new();

    /// <summary>
    /// <paramref name="filePath"/>에 값을 저장/로드하는 저장소를 만듭니다. 파일이 이미 있으면 생성자에서
    /// 바로 읽어 메모리 캐시를 채우고, 없으면 빈 상태로 시작합니다(첫 <see cref="Set"/> 때 새로 만들어짐).
    /// </summary>
    public FileContextStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    /// <inheritdoc/>
    public T? Get<T>(string scope, string scopeId, string key)
    {
        if (!_data.TryGetValue((scope, scopeId, key), out var value)) return default;
        if (value is T typed) return typed;

        // 파일에서 막 불러온 값은 원본 CLR 타입이 아니라 JToken으로 남아있을 수 있다(위 remarks 참고) —
        // 요청한 T로 재변환을 시도하고, 실패하면 InMemoryContextStore와 동일하게 기본값을 반환한다.
        try
        {
            if (value is JToken token) return token.ToObject<T>();
            return (T?)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc/>
    public void Set(string scope, string scopeId, string key, object? value)
    {
        _data[(scope, scopeId, key)] = value;
        Save();
    }

    /// <inheritdoc/>
    public IEnumerable<string> Keys(string scope, string scopeId) =>
        _data.Keys.Where(k => k.Scope == scope && k.ScopeId == scopeId).Select(k => k.Key).ToList();

    /// <summary>파일이 있으면 통째로 읽어 <see cref="_data"/>를 채운다. 파일이 없거나 비어있으면 그냥 빈 상태로 둔다.</summary>
    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json)) return;

        var entries = JsonConvert.DeserializeObject<List<ContextEntry>>(json);
        if (entries is null) return;

        foreach (var entry in entries)
        {
            _data[(entry.Scope, entry.ScopeId, entry.Key)] = entry.Value;
        }
    }

    /// <summary>현재 <see cref="_data"/> 전체를 JSON으로 직렬화해 <see cref="_filePath"/>에 통째로 다시 쓴다.</summary>
    private void Save()
    {
        lock (_fileLock)
        {
            var entries = _data
                .Select(kv => new ContextEntry(kv.Key.Scope, kv.Key.ScopeId, kv.Key.Key, kv.Value))
                .ToList();
            var json = JsonConvert.SerializeObject(entries, Formatting.Indented);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_filePath, json);
        }
    }

    /// <summary>파일에 저장되는 값 1건의 형태 — (scope, scopeId, key)를 튜플 대신 JSON 친화적인 필드로 풀어둔다.</summary>
    private sealed record ContextEntry(string Scope, string ScopeId, string Key, object? Value);
}
