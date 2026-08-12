using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NodeSharp.Nodes.Function;
using NodeSharp.Nodes.Inject;
using NodeSharp.Nodes.Switch;
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
/// (EC-01b) 카드를 누른 채 일정 거리 이상 움직이면(<see cref="OnCardPreviewMouseMove"/>) WPF 표준
/// 드래그 앤 드롭을 시작해 <see cref="FlowCanvasView"/>의 캔버스가 받을 수 있게 합니다.
/// <see cref="MarkTypeUsed"/>를 공개 메서드로 열어, 캔버스에 실제로 노드가 배치됐을 때도(클릭이
/// 아니라 드래그로 놓았을 때도) "최근 사용"에 반영되게 했습니다.
/// </summary>
public partial class PaletteView : UserControl
{
    private readonly NodeTypeRegistry _registry = new(contractsVersion: "1.0.0");
    private readonly PaletteRecentUsageTracker _recentUsage = new();
    private readonly List<PaletteNodeCardViewModel> _allCards = new();
    private Point _dragStartPoint;

    /// <summary>
    /// XAML 컨트롤을 초기화하고, 현재 등록된 노드 타입으로 팔레트를 채웁니다.
    /// (EC-01c) 이전에는 <see cref="_registry"/>를 만들기만 하고 아무 노드 타입도 스캔해 넣지 않아
    /// 팔레트가 Phase 7 이후에도 계속 비어 있던 공백이 있었습니다 — 여기서 코어 노드 플러그인
    /// 어셈블리를 직접 스캔해 채웁니다. 새 노드 타입 프로젝트가 추가될 때마다 이 목록에도 한 줄씩
    /// 추가해야 팔레트에 나타납니다. (FN-01) FunctionNodeType 추가.
    /// </summary>
    public PaletteView()
    {
        InitializeComponent();
        _registry.ScanAssembly(typeof(InjectNodeType).Assembly);
        _registry.ScanAssembly(typeof(SwitchNodeType).Assembly);
        _registry.ScanAssembly(typeof(FunctionNodeType).Assembly);
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
    /// 즉시 다시 그려 "최근 사용" 섹션에 반영합니다. 실제 드래그가 발생한 클릭은 보통 이 이벤트까지
    /// 도달하지 않지만(WPF DragDrop이 마우스를 가져감), 도달하더라도 <see cref="MarkTypeUsed"/>가
    /// 중복 호출을 그대로 허용하므로(이미 맨 앞이면 다시 맨 앞) 문제없습니다.
    /// </summary>
    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string typeName })
        {
            MarkTypeUsed(typeName);
        }
    }

    /// <summary>드래그 시작 좌표를 기억합니다(다음 <see cref="OnCardPreviewMouseMove"/>의 임계값 판정용).</summary>
    private void OnCardPreviewMouseDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(null);

    /// <summary>
    /// (EC-01b) 왼쪽 버튼을 누른 채 <see cref="SystemParameters.MinimumHorizontalDragDistance"/>/
    /// <see cref="SystemParameters.MinimumVerticalDragDistance"/>를 넘게 움직이면 이 카드의
    /// TypeName(Border.Tag)을 문자열 데이터로 담아 <see cref="DragDrop.DoDragDrop"/>을 시작합니다.
    /// 임계값을 넘지 않으면 아무 것도 하지 않고, 이후 <see cref="OnCardClicked"/>가 평범한 클릭으로
    /// 처리합니다.
    /// </summary>
    private void OnCardPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { Tag: string typeName } element)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(element, typeName, DragDropEffects.Copy);
    }

    /// <summary>
    /// (EC-01b) <see cref="FlowCanvasView"/>가 캔버스에 실제로 노드를 배치한 뒤 호출합니다.
    /// <see cref="PaletteRecentUsageTracker.MarkUsed"/>로 기록하고 화면을 다시 그려 "최근 사용"
    /// 섹션에 반영합니다(검색어가 비어 있을 때만 보이는 섹션이라, 검색 중이면 이번 갱신은 눈에 보이지
    /// 않다가 검색어를 지우면 나타납니다).
    /// </summary>
    public void MarkTypeUsed(string typeName)
    {
        _recentUsage.MarkUsed(typeName);
        ApplyFilter(SearchBox.Text);
    }
}
