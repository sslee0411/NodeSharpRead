using System.Collections.Concurrent;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 태그 값 캐시
/// 역활 및 기능 : DeviceMapPoller가 배치로 읽은 태그 값을 태그별로 보관하는 스레드 세이프 캐시
///
/// (ED-D06b) 02번 설계문서 8번 탭 카드9(DeviceMap 배치 폴링 엔진 + Tag 값 캐시) 원본 스니펫의
/// <c>ConcurrentDictionary&lt;string, object?&gt; _cache</c>를 <see cref="DeviceMapPoller"/>가 직접 들고
/// 있는 private 필드가 아니라, 별도 클래스로 분리했습니다 — 카드9 코드 자체 주석("PlcTagReadNode는
/// 이제 GetCached()로 즉시 응답")이 암시하듯 여러 소비자(DeviceMapPoller가 여러 개인 배포에서 각각
/// 자신의 디바이스맵을 읽어 넣고, 나중에 PlcTagReadNode 등 다른 코드가 태그 Id만으로 조회)가
/// 같은 캐시를 공유해야 하기 때문입니다. 이 Step(ED-D06b)의 완료 기준 자체는 "배치 폴링이 캐시를
/// 경유해 통신 횟수를 줄이는지"만 요구해 PlcTagReadNode 연동은 범위 밖으로 남겨두지만, 그 연동이
/// 쉽도록 처음부터 별도 공유 가능한 타입으로 설계했습니다.
/// </summary>
public sealed class TagValueCache
{
    private readonly ConcurrentDictionary<string, object?> _values = new();

    /// <summary>지정한 태그의 최신값을 갱신합니다. DeviceMapPoller가 배치 읽기 결과를 반영할 때 사용합니다.</summary>
    public void Set(string tagId, object? value) => _values[tagId] = value;

    /// <summary>지정한 태그의 캐시된 최신값을 반환합니다. 아직 한 번도 갱신되지 않았으면 <c>null</c>을 반환합니다.</summary>
    public object? GetCached(string tagId) => _values.TryGetValue(tagId, out var value) ? value : null;

    /// <summary>지정한 태그가 캐시에 값을 가지고 있는지(한 번이라도 갱신된 적 있는지) 확인합니다.</summary>
    public bool TryGetCached(string tagId, out object? value) => _values.TryGetValue(tagId, out value);

    /// <summary>현재 캐시에 값이 등록된 태그 개수입니다(테스트·진단용).</summary>
    public int Count => _values.Count;
}
