namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 팔레트 최근 사용 추적기
/// 역활 및 기능 : 팔레트에서 클릭(사용)한 노드 타입 이름을 최근 사용 순서로 최대 5개까지 기억하는 순수 로직
///
/// 사용자가 팔레트 카드를 클릭할 때마다 <see cref="MarkUsed"/>를 호출하면, 그 타입 이름을 목록 맨
/// 앞으로 옮기고(이미 있으면 중복 추가 대신 이동) 5개를 넘으면 가장 오래된 것부터 잘라냅니다(02번
/// 문서 9번 탭 카드10 "최근 사용한 노드 5개는 검색창 비어있을 때 상단 고정 표시"). WPF 타입을 전혀
/// 참조하지 않는 순수 C# 로직이지만, NodeSharp.Editor 프로젝트 자체가 net8.0-windows+UseWPF라 이
/// Linux 샌드박스에서는 프로젝트 빌드조차 불가능해 xUnit 대상이 될 수 없습니다(ED-B0~ED-B2b와 동일한
/// 제약) — 로직만 별도 클래스로 분리해두면 나중에 Windows 환경에서 수동으로라도 동작을 눈으로
/// 확인하기 쉽게 하기 위함입니다.
/// </summary>
public sealed class PaletteRecentUsageTracker
{
    private const int MaxCount = 5;
    private readonly List<string> _recentTypeNames = new();

    /// <summary>가장 최근 사용한 것이 맨 앞에 오는 타입 이름 목록입니다(최대 <see cref="MaxCount"/>개).</summary>
    public IReadOnlyList<string> RecentTypeNames => _recentTypeNames;

    /// <summary>
    /// <paramref name="typeName"/>을 "방금 사용함"으로 기록합니다. 이미 목록에 있으면 맨 앞으로
    /// 옮기고, 없으면 맨 앞에 추가한 뒤 <see cref="MaxCount"/>를 넘는 나머지(가장 오래된 것부터)를
    /// 잘라냅니다.
    /// </summary>
    public void MarkUsed(string typeName)
    {
        _recentTypeNames.Remove(typeName);
        _recentTypeNames.Insert(0, typeName);

        while (_recentTypeNames.Count > MaxCount)
        {
            _recentTypeNames.RemoveAt(_recentTypeNames.Count - 1);
        }
    }
}
