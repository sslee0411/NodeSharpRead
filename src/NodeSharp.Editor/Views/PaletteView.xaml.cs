using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NodeSharp.Registry;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 노드 팔레트 뷰
/// 역활 및 기능 : FlowCanvasView 좌측에 항상 떠 있는 노드 팔레트 — 검색·최근 사용 섹션 제공
///
/// (EC-01a) <c>NodeTypeRegistry.Descriptors</c>를 카드로 나열하고, 검색창에 입력한 문자열로
/// TypeName/Category를 필터링합니다(<c>INodeTypeDescriptor</c>에 별도 "표시 이름" 필드가 없어
/// TypeName을 그대로 표시·검색 대상으로 씁니다, 02번 문서 2번 탭 카드1). 카드를 클릭하면
/// <see cref="PaletteRecentUsageTracker"/>에 "사용"으로 기록되어 "최근 사용" 섹션(검색어가 비어
/// 있을 때만 표시) 맨 앞에 나타납니다. 이 시점엔 Phase 7 이전이라 등록된 노드 타입이 없어 팔레트가
/// 비어 있는 것이 정상이며(03번 Step맵 EC-01a desc), 실제 필터링·최근 사용 동작의 전체 확인은
/// Phase 7에서 노드 타입이 채워진 뒤 NR-09(캔버스 UX 마감, 이미 03번 Step맵에 이 목적으로 존재)에서
/// 다시 확인합니다.
/// </summary>
public partial class PaletteView : UserControl
{
    private readonly NodeTypeRegistry _registry = new(contractsVersion: "1.0.0");
    private readonly PaletteRecentUsageTracker _recentUsage = new();
    private readonly List<PaletteNodeCardViewModel> _allCards = new();

    /// <summary>XAML 컨트롤을 초기화하고, 현재 등록된 노드 타입으로 팔레트를 채웁니다(지금은 보통 0개).</summary>
    public PaletteView()
    {
        InitializeComponent();
        RefreshAllCards();
        ApplyFilter(string.Empty);
    }

    /// <summary><c>NodeTypeRegistry.Descriptors</c>를 다시 읽어 카드 목록을 갱신합니다.</summary>
    private void RefreshAllCards()
    {
        _allCards.Clear();
        foreach (var descriptor in _registry.Descriptors.Values.OrderBy(d => d.Category).ThenBy(d => d.TypeName))
        {
            _allCards.Add(new PaletteNodeCardViewModel(descriptor.TypeName, descriptor.Category, descriptor.IconGlyph));
        }

        EmptyHint.Visibility = _allCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>검색창 텍스트가 바뀔 때마다 필터를 다시 적용합니다.</summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    /// <summary>
    /// <paramref name="query"/>가 비어 있으면 "최근 사용" 섹션을 보여주고 전체 카드를 나열하며,
    /// 비어 있지 않으면 "최근 사용" 섹션을 숨기고 TypeName 또는 Category에 <paramref name="query"/>가
    /// 포함된 카드만 나열합니다(대소문자 구분 없음).
    /// </summary>
    private void ApplyFilter(string query)
    {
        var trimmed = query.Trim();
        var isEmpty = trimmed.Length == 0;

        RecentHeader.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RecentItemsControl.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RecentItemsControl.ItemsSource = isEmpty ? BuildRecentCards() : null;

        AllItemsControl.ItemsSource = _allCards
            .Where(c => isEmpty
                || c.TypeName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary><see cref="_recentUsage"/>가 기억하는 최근 사용 순서대로 카드 정보를 되짚어 만듭니다.</summary>
    private List<PaletteNodeCardViewModel> BuildRecentCards()
    {
        var result = new List<PaletteNodeCardViewModel>();
        foreach (var typeName in _recentUsage.RecentTypeNames)
        {
            var card = _allCards.FirstOrDefault(c => c.TypeName == typeName);
            if (card is not null)
            {
                result.Add(card);
            }
        }

        return result;
    }

    /// <summary>
    /// 카드(팔레트 항목)를 클릭하면 그 카드의 TypeName(Border.Tag)을 "사용함"으로 기록하고, 화면을
    /// 즉시 다시 그려 "최근 사용" 섹션에 반영합니다.
    /// </summary>
    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string typeName })
        {
            _recentUsage.MarkUsed(typeName);
            ApplyFilter(SearchBox.Text);
        }
    }
}
