using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NodeSharp.Editor.Core.Config;
using NodeSharp.Editor.Structure;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 구조 설정 뷰
/// 역활 및 기능 : 장비→PLC→디바이스맵→태그→스케일→알람 6단계 고정 트리(<see cref="StructureTreeNode"/>
/// 기반)를 렌더링하고, 각 단계 노드의 추가/삭제/이름 변경을 처리하는 화면
///
/// (ED-D01) 완료 기준("6단계 트리가 StructureTreeNode로 렌더링되고, 각 단계 노드 추가/삭제가 정상
/// 동작하는지")을 만족합니다. 실제 속성 편집 폼은 ED-D02a/b, TagRef 연동은 ED-D04 범위입니다.
/// (ED-B2a) 02번 문서 8번 탭 카드15의 "항상 분할 도킹" 설계에 따라 <see cref="FlowCanvasView"/>와
/// GridSplitter를 사이에 두고 항상 동시에 표시됩니다.
/// (ED-D03) device.json 저장/로드가 추가됐습니다 — <see cref="LoadDeviceTreeAsync"/>가 <see cref="OnLoaded"/>에서
/// 자동 호출되고(<see cref="FlowCanvasView"/>의 flows.json 자동 로드와 동일한 관례), <see cref="SaveDeviceTreeAsync"/>는
/// <c>MainWindow</c>의 "파일 → 저장"/Ctrl+S(기존 <c>FlowCanvas.SaveFlowAsync()</c>와 같은 핸들러)가
/// 함께 호출합니다 — 이 프로젝트는 "저장"을 flows.json/device.json 둘로 나누지 않고 하나의 사용자
/// 동작(Ctrl+S)으로 통합해 다루는 편이(각 트리마다 별도 저장 버튼을 요구하는 것보다) Node-RED류
/// 편집기 사용자에게 더 익숙하다고 판단했습니다.
/// (ED-D12) <see cref="TagNodeSelected"/> 이벤트가 추가됐습니다 — 태그 노드를 선택하면 그 TagId를
/// 실어 발생시키고(선택 해제·다른 노드 선택 시 <c>null</c>), <c>MainWindow</c>가 이를
/// <c>FlowCanvasView.HighlightNodesByTagRef</c>로 연결해 그 태그를 참조하는 캔버스 노드를 잠깐
/// 강조합니다("② 캔버스 → 구조 트리" 역방향인 <see cref="FlowCanvasView"/>의 TagRef override
/// 조회(EC-19)와 짝을 이루는 "① 구조 트리 → 캔버스" 방향).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>렌더링 방식</b>: WPF 기본 <see cref="TreeView"/>/<c>HierarchicalDataTemplate</c> 대신,
/// <see cref="DebugSidebarView"/>(LK-02b)와 동일하게 <see cref="Border"/>+<see cref="StackPanel"/>을
/// 코드비하인드에서 직접 그리는 "평탄화 렌더링"(각 행을 계층 없이 <see cref="TreePanel"/>에 순서대로
/// 추가하고, 계층은 행 왼쪽 여백(들여쓰기)으로만 표현)을 씁니다 — 이 프로젝트가 이미 두 차례
/// 겪은 "WPF 기본 컨트롤이 Background/Foreground를 무시하고 SystemColors 크롬을 그대로 쓰는" 부류의
/// 버그(<c>MainWindow.xaml</c> EC-11 TabControl, <c>NodePropertyDialog</c> PropCombo ComboBox)를
/// TreeView에서 또 만들 위험을 원천적으로 피하기 위한 선택입니다.</item>
/// <item><b>추가</b>: 헤더의 "+ 장비" 버튼(루트, <see cref="DeviceNode"/> 전용)과 각 행의 우클릭
/// 컨텍스트 메뉴(<see cref="StructureTreeNode.AllowedChildTypes"/>에 있는 타입마다 메뉴 항목 1개)
/// 두 경로로 자식을 추가합니다. 새로 추가된 노드는 <see cref="TypeLabels"/> 기반 기본 이름("새 태그
/// 1" 등, 타입별 순번)으로 생성된 뒤 곧바로 <see cref="BeginRename"/>으로 이름 입력 상태가 됩니다 —
/// 기본 이름만으로는 트리에서 서로 구분하기 어렵기 때문입니다.</item>
/// <item><b>삭제</b>: 컨텍스트 메뉴 "삭제" — 확인 팝업 없이 즉시 제거합니다(자식까지 함께 제거,
/// <see cref="StructureTreeNode.Children"/>이 그대로 달려 있으므로 자동으로 함께 사라짐). 되돌리기는
/// 이 Step 범위 밖입니다(캔버스 쪽 <c>CommandHistory</c>/Undo·Redo는 플로우 캔버스 전용이라 이
/// 트리에는 아직 연결돼 있지 않음 — 필요해지면 별도 Step).</item>
/// <item><b>빠른 이름 변경</b>: 컨텍스트 메뉴 "이름 변경"으로 시작하는 인라인 편집 — Enter 또는
/// 포커스를 잃으면 커밋되고, 빈 문자열이면 이전 이름을 그대로 유지합니다.</item>
/// <item><b>(ED-D02a/b) 속성 편집</b>: 행을 더블클릭하거나 컨텍스트 메뉴 "속성 편집"을 누르면
/// <see cref="StructureNodePropertyDialog"/>가 모달로 뜹니다 — <see cref="FlowCanvasView"/>가
/// 캔버스 노드 카드를 더블클릭하면 <see cref="NodePropertyDialog"/>를 띄우는 것(EC-03)과 동일한
/// 앱 전체 관례입니다. 다이얼로그가 "완료"로 닫히면(<see cref="OpenPropertyDialog"/> 참고)
/// 노드가 그 자리에서 이미 수정된 상태이므로 <see cref="RenderTree"/>로 이름 변경 등을 즉시
/// 반영합니다(완료 기준 "값 변경 후 저장하면 트리에 즉시 반영").</item>
/// <item><b>(ED-D04) TagRef 연동</b>: <see cref="RenderTree"/>가 호출될 때마다(추가/삭제/이름변경/
/// 속성편집 — 사실상 트리가 바뀔 때마다) <see cref="TagCatalog.Update"/>로 현재 태그 목록(Id+표시
/// 경로)을 갱신해둡니다 — 캔버스 노드(<see cref="NodePropertyDialog"/>가 그리는
/// PropertyFieldType.TagRef 필드)가 이 값을 읽어 태그 선택 콤보박스를 채웁니다. 02번 설계문서 9번
/// 탭 카드5는 "팝업"으로 태그를 고른다고 서술하지만, 이 프로젝트는 새 팝업 창을 만들지 않고 이미
/// 항상 열려있는 이 탭(ED-B2a "항상 분할 도킹" 설계)의 데이터를 그대로 재사용하는 쪽을 택했습니다
/// — 02번 문서 뒤쪽의 "② 캔버스 → 구조 트리" 내비게이션 절도 이미 이 방식(팝업 없음)으로 서술이
/// 바뀌어 있어, 이 프로젝트의 실제 아키텍처와 일치하는 쪽을 따랐습니다(자세한 판단 경위는 03번 Step맵
/// ED-D04 항목 참고).</item>
/// </list>
/// </remarks>
public partial class StructureView : UserControl
{
    /// <summary>트리 루트(1단계, <see cref="DeviceNode"/>) 목록 — ED-D03(device.json 저장)이 이 값을 직렬화 대상으로 그대로 읽습니다.</summary>
    public ObservableCollection<StructureTreeNode> Devices { get; } = new();

    /// <summary>노드 타입 → 사용자에게 보여줄 한글 라벨("추가" 메뉴 항목·기본 이름 접두어에 사용).</summary>
    private static readonly IReadOnlyDictionary<Type, string> TypeLabels = new Dictionary<Type, string>
    {
        [typeof(DeviceNode)] = "장비",
        [typeof(PlcNode)] = "PLC",
        [typeof(DeviceMapNode)] = "디바이스맵",
        [typeof(TagNode)] = "태그",
        [typeof(ScaleNode)] = "스케일",
        [typeof(AlarmNode)] = "알람",
    };

    /// <summary>노드 타입 → 새 인스턴스 생성 팩토리 — "추가" 메뉴가 <see cref="StructureTreeNode.AllowedChildTypes"/>의 각 타입을 이 표로 실제 인스턴스로 만듭니다.</summary>
    private static readonly IReadOnlyDictionary<Type, Func<StructureTreeNode>> Factories = new Dictionary<Type, Func<StructureTreeNode>>
    {
        [typeof(DeviceNode)] = () => new DeviceNode(),
        [typeof(PlcNode)] = () => new PlcNode(),
        [typeof(DeviceMapNode)] = () => new DeviceMapNode(),
        [typeof(TagNode)] = () => new TagNode(),
        [typeof(ScaleNode)] = () => new ScaleNode(),
        [typeof(AlarmNode)] = () => new AlarmNode(),
    };

    /// <summary>타입별 "새 XXX N" 기본 이름에 쓰이는 순번 — 삭제해도 감소하지 않습니다(항상 늘어나는 카운터라 재사용 중 이름 충돌을 피함).</summary>
    private readonly Dictionary<Type, int> _nameCounters = new();

    /// <summary>노드별 펼침 상태 — 값이 없으면(새로 추가된 노드 포함) 기본 true(펼침)로 취급합니다.</summary>
    private readonly Dictionary<StructureTreeNode, bool> _expanded = new();

    /// <summary><see cref="RenderTree"/>가 매번 다시 채우는, 노드 → 그 행의 이름 표시 영역(<see cref="StackPanel"/>) 매핑 — <see cref="BeginRename"/>이 그 행을 찾는 데 씁니다.</summary>
    private readonly Dictionary<StructureTreeNode, StackPanel> _rowContentByNode = new();

    /// <summary>현재 선택된 노드(단일 선택) — 없으면 null.</summary>
    private StructureTreeNode? _selectedNode;

    /// <summary>
    /// (ED-D12, ★ 완료 기준 — "구조 트리에서 태그 선택 시 사용 중인 캔버스 노드를 하이라이트")
    /// <see cref="Select"/>가 호출될 때마다 발생 — 선택된 노드가 <see cref="TagNode"/>이면 그
    /// <see cref="StructureTreeNode.Id"/>(TagId)를, 그 외(다른 5단계 노드 선택)면 <c>null</c>을
    /// 전달합니다. <c>MainWindow</c>가 <c>FlowCanvasView.HighlightNodesByTagRef</c>로 그대로 넘겨
    /// 캔버스 쪽을 반영합니다 — 이 뷰 자신은 <see cref="FlowCanvasView"/>를 몰라도 되는 얇은 이벤트
    /// 발행만 담당합니다(<c>FlowCanvasView.SelectionChanged</c>가 <c>MainWindow</c>를 거쳐 Information
    /// 패널에 연결되는 것과 동일한 방향의 "뷰는 서로 직접 참조하지 않는다" 원칙).
    /// </summary>
    public event Action<string?>? TagNodeSelected;

    /// <summary>(ED-D03) device.json이 저장될 폴더 — <c>FlowCanvasView.DataDirectory</c>와 동일한 관례(기본값
    /// <see cref="AppContext.BaseDirectory"/>)입니다. <c>MainWindow</c>가 둘 중 어느 쪽도 별도로 지정하지
    /// 않으므로, 실제로는 항상 같은 기본값을 가리켜 flows.json과 device.json이 같은 폴더에 나란히 저장됩니다.</summary>
    public string DataDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>(ED-D03) device.json 저장/로드 전용 창구 — <see cref="FlowStore"/>와 동일한 얇은 래퍼 패턴.</summary>
    private readonly DeviceStore _deviceStore = new();

    /// <summary>XAML 컨트롤을 초기화하고 "+ 장비" 버튼을 연결한 뒤, 빈 트리를 1회 렌더링합니다(WPF 표준 패턴). (ED-D03) <see cref="OnLoaded"/>를 구독해 컨트롤이 화면에 뜨는 시점에 device.json을 자동으로 불러옵니다.</summary>
    public StructureView()
    {
        InitializeComponent();
        AddDeviceButton.MouseLeftButtonDown += (_, _) => AddRoot();
        RenderTree();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// (ED-D03) <see cref="FlowCanvasView"/>의 <c>OnLoaded</c>와 동일한 이유로(<c>async void</c> 이벤트
    /// 핸들러의 처리되지 않은 예외는 앱 전체를 크래시시키는 WPF의 잘 알려진 함정 — v2.53 버그 수정
    /// 참고) 예외를 여기서 한 번 더 감싸, device.json을 못 읽어도 빈 트리로 계속 사용할 수 있게 합니다.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadDeviceTreeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"device.json 불러오기 중 오류가 발생했습니다. 빈 트리로 시작합니다.\n{ex.Message}",
                "불러오기 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// (ED-D03) <see cref="DataDirectory"/>\device.json을 읽어 저장된 트리가 있으면 <see cref="Devices"/>를
    /// 그 내용으로 완전히 교체하고 다시 그립니다. 파일이 없거나(최초 실행) 비어 있으면(장비 0개) 아무
    /// 것도 하지 않습니다(<see cref="FlowStore.LoadAsync"/>가 null이면 그대로 두는 것과 동일한 관례).
    /// </summary>
    public async Task LoadDeviceTreeAsync()
    {
        var tree = await _deviceStore.LoadAsync(DataDirectory);
        if (tree is null || tree.Devices.Count == 0)
        {
            return;
        }

        Devices.Clear();
        foreach (var node in StructureTreeMapper.FromDto(tree))
        {
            Devices.Add(node);
        }

        RenderTree();
    }

    /// <summary>
    /// (ED-D03) 지금 메모리에 있는 <see cref="Devices"/>(6단계 트리 전체)를 <see cref="StructureTreeMapper.ToDto"/>로
    /// <c>DeviceTreeDto</c>로 변환해 <see cref="DataDirectory"/>\device.json에 원자적으로 저장합니다
    /// (<see cref="DeviceStore.SaveAsync"/> — .tmp에 먼저 전부 쓴 뒤 원본과 한 번에 바꿔치기하므로 저장
    /// 도중 강제 종료돼도 기존 device.json은 손상되지 않습니다). <c>MainWindow</c>의 "파일 → 저장"
    /// 메뉴/Ctrl+S가 <c>FlowCanvas.SaveFlowAsync()</c>와 함께 이 메서드를 호출합니다.
    /// </summary>
    public async Task SaveDeviceTreeAsync()
    {
        var tree = StructureTreeMapper.ToDto(Devices);
        await _deviceStore.SaveAsync(tree, DataDirectory);
    }

    /// <summary>헤더 "+ 장비" — 새 <see cref="DeviceNode"/>를 루트 목록 맨 끝에 추가하고 즉시 이름 변경 상태로 만듭니다.</summary>
    private void AddRoot()
    {
        var node = CreateWithDefaultName(typeof(DeviceNode));
        Devices.Add(node);
        RenderTree();
        BeginRename(node);
    }

    /// <summary>컨텍스트 메뉴 "OO 추가" — <paramref name="childType"/> 타입의 새 자식을 <paramref name="parent"/>에 추가하고, 부모를 펼친 뒤 즉시 이름 변경 상태로 만듭니다.</summary>
    private void AddChild(StructureTreeNode parent, Type childType)
    {
        var child = CreateWithDefaultName(childType);
        parent.Children.Add(child);
        _expanded[parent] = true;
        RenderTree();
        BeginRename(child);
    }

    /// <summary><paramref name="type"/>를 <see cref="Factories"/>로 생성하고 <see cref="TypeLabels"/>+<see cref="_nameCounters"/> 기반 기본 이름("새 태그 1" 등)을 채웁니다.</summary>
    private StructureTreeNode CreateWithDefaultName(Type type)
    {
        var node = Factories[type]();
        _nameCounters[type] = _nameCounters.GetValueOrDefault(type) + 1;
        node.Name = $"새 {TypeLabels[type]} {_nameCounters[type]}";
        return node;
    }

    /// <summary>컨텍스트 메뉴 "삭제" — <paramref name="node"/>를 루트 목록 또는 어느 조상의 <see cref="StructureTreeNode.Children"/>에서 찾아 제거합니다(자식이 있으면 함께 사라짐, 확인 팝업 없음).</summary>
    private void DeleteNode(StructureTreeNode node)
    {
        if (!Devices.Remove(node))
        {
            RemoveFromDescendants(Devices, node);
        }

        if (ReferenceEquals(_selectedNode, node))
        {
            _selectedNode = null;
        }

        RenderTree();
    }

    /// <summary><paramref name="target"/>을 <paramref name="siblings"/> 또는 그 자손들의 Children에서 재귀적으로 찾아 제거합니다. 찾아서 제거했으면 true.</summary>
    private static bool RemoveFromDescendants(ObservableCollection<StructureTreeNode> siblings, StructureTreeNode target)
    {
        foreach (var sibling in siblings)
        {
            if (sibling.Children.Remove(target))
            {
                return true;
            }

            if (RemoveFromDescendants(sibling.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><see cref="Devices"/> 전체를 <see cref="TreePanel"/>에 다시 그립니다 — 추가/삭제/펼침전환/선택마다 호출되는 단일 갱신 지점(EC-05 "데이터를 바꾸고 한 메서드로 화면을 맞춘다" 원칙과 동일). (ED-D04) 그리기 전에 <see cref="TagCatalog"/>도 함께 최신화합니다(클래스 remarks 참고).</summary>
    private void RenderTree()
    {
        TagCatalog.Update(FlattenTags(Devices));

        TreePanel.Children.Clear();
        _rowContentByNode.Clear();

        foreach (var root in Devices)
        {
            RenderNode(root, 0);
        }

        var hasAny = Devices.Count > 0;
        EmptyHint.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        TreeScroll.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// (ED-D04) <paramref name="roots"/>(보통 <see cref="Devices"/>) 안의 모든 <see cref="TagNode"/>를
    /// "장비/PLC/디바이스맵/태그" 형태의 표시 경로와 함께 평탄화합니다 — <see cref="TagCatalog.Update"/>가
    /// 이 결과로 <see cref="NodePropertyDialog"/>(EC-03)의 TagRef 콤보박스를 채웁니다.
    /// </summary>
    private static List<TagCatalogEntry> FlattenTags(IEnumerable<StructureTreeNode> roots)
    {
        var result = new List<TagCatalogEntry>();

        void Walk(StructureTreeNode node, string pathPrefix)
        {
            var path = pathPrefix.Length == 0 ? node.Name : $"{pathPrefix}/{node.Name}";
            if (node is TagNode)
            {
                result.Add(new TagCatalogEntry(node.Id, path));
            }

            foreach (var child in node.Children)
            {
                Walk(child, path);
            }
        }

        foreach (var root in roots)
        {
            Walk(root, string.Empty);
        }

        return result;
    }

    /// <summary><paramref name="node"/> 행 1개를 <see cref="TreePanel"/>에 추가하고, 펼쳐져 있으면(<see cref="_expanded"/>) 자식들도 <paramref name="depth"/>+1로 재귀 렌더링합니다.</summary>
    private void RenderNode(StructureTreeNode node, int depth)
    {
        var hasChildren = node.Children.Count > 0;
        var isExpanded = _expanded.TryGetValue(node, out var expanded) ? expanded : true;

        var row = new Border
        {
            Background = ReferenceEquals(_selectedNode, node) ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2, 3, 2, 3),
            Margin = new Thickness(depth * 18, 0, 0, 1),
            Cursor = Cursors.Hand,
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        var toggle = new TextBlock
        {
            Text = hasChildren ? (isExpanded ? "▼" : "▶") : " ",
            Width = 14,
            FontSize = 10,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = hasChildren ? Cursors.Hand : Cursors.Arrow,
        };
        if (hasChildren)
        {
            toggle.MouseLeftButtonDown += (_, e) =>
            {
                _expanded[node] = !isExpanded;
                RenderTree();
                e.Handled = true; // 토글 클릭이 아래 row.MouseLeftButtonDown(선택)까지 겹쳐 발생하지 않게 함.
            };
        }
        content.Children.Add(toggle);

        content.Children.Add(new TextBlock
        {
            Text = node.IconGlyph,
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var nameText = new TextBlock
        {
            Text = node.Name,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenPropertyDialog(node);
                e.Handled = true;
            }
        };
        content.Children.Add(nameText);

        row.Child = content;
        row.MouseLeftButtonDown += (_, _) => Select(node);
        row.MouseRightButtonDown += (_, _) => Select(node); // 우클릭도 먼저 선택 반영 — 어떤 행에 메뉴를 띄우는지 시각적으로 분명하게.
        row.ContextMenu = BuildContextMenu(node);

        TreePanel.Children.Add(row);
        _rowContentByNode[node] = content;

        if (hasChildren && isExpanded)
        {
            foreach (var child in node.Children)
            {
                RenderNode(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// <paramref name="node"/>를 선택 노드로 표시하고 다시 그립니다(선택 배경 갱신). (ED-D12)
    /// 이어서 <see cref="TagNodeSelected"/>를 발생시켜, <paramref name="node"/>가 <see cref="TagNode"/>이면
    /// 그 Id를, 아니면 <c>null</c>을 알립니다(클래스 자체 주석 참고).
    /// </summary>
    private void Select(StructureTreeNode node)
    {
        _selectedNode = node;
        RenderTree();
        TagNodeSelected?.Invoke(node is TagNode ? node.Id : null);
    }

    /// <summary>
    /// <paramref name="node"/> 행의 우클릭 메뉴를 만듭니다 — <see cref="StructureTreeNode.AllowedChildTypes"/>마다
    /// "OO 추가" 항목 1개(잎 노드는 이 구간 자체가 비어 생략), 항상 "이름 변경"·"삭제" 2개.
    /// </summary>
    private ContextMenu BuildContextMenu(StructureTreeNode node)
    {
        var menu = new ContextMenu();

        foreach (var childType in node.AllowedChildTypes)
        {
            var addItem = new MenuItem { Header = $"{TypeLabels[childType]} 추가" };
            addItem.Click += (_, _) => AddChild(node, childType);
            menu.Items.Add(addItem);
        }

        if (node.AllowedChildTypes.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var propertyItem = new MenuItem { Header = "속성 편집" };
        propertyItem.Click += (_, _) => OpenPropertyDialog(node);
        menu.Items.Add(propertyItem);

        var renameItem = new MenuItem { Header = "이름 변경" };
        renameItem.Click += (_, _) => BeginRename(node);
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = "삭제" };
        deleteItem.Click += (_, _) => DeleteNode(node);
        menu.Items.Add(deleteItem);

        return menu;
    }

    /// <summary>
    /// (ED-D02a/b) <paramref name="node"/>의 <see cref="StructureNodePropertyDialog"/>를 모달로 띄웁니다.
    /// "완료"로 닫히면(<see cref="StructureNodePropertyDialog.Saved"/>) 다이얼로그가 이미 <paramref name="node"/>를
    /// 그 자리에서 수정해뒀으므로 <see cref="RenderTree"/>만 호출해 이름 등 변경 사항을 화면에 즉시
    /// 반영합니다(완료 기준). "취소"로 닫히면 아무 것도 바뀌지 않았으므로 다시 그리지 않습니다.
    /// </summary>
    private void OpenPropertyDialog(StructureTreeNode node)
    {
        var dialog = new StructureNodePropertyDialog(node) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        if (dialog.Saved)
        {
            RenderTree();
        }
    }

    /// <summary>
    /// <paramref name="node"/> 행의 이름 <see cref="TextBlock"/>을 <see cref="TextBox"/>로 바꿔치기해 즉시
    /// 편집 가능한 상태로 만듭니다 — 커밋(Enter 또는 포커스 상실)되면 <see cref="CommitRename"/>이 값을
    /// 반영하고 <see cref="RenderTree"/>로 원래 표시로 되돌립니다. 빈 문자열로 커밋하면 기존 이름을
    /// 그대로 유지합니다(빈 이름 노드가 트리에서 식별 불가능해지는 것을 방지).
    /// </summary>
    private void BeginRename(StructureTreeNode node)
    {
        if (!_rowContentByNode.TryGetValue(node, out var content) || content.Children.Count == 0)
        {
            return; // 아직 렌더링되지 않은 행(예: 접힌 부모 안의 자식)이면 조용히 무시.
        }

        var nameIndex = content.Children.Count - 1; // RenderNode가 마지막 자식으로 이름 TextBlock을 추가함.
        var editBox = new TextBox
        {
            Text = node.Name,
            Width = 140,
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
        };

        // RenderTree()가 이 TextBox를 시각 트리에서 제거하면 WPF가 뒤이어 LostFocus를 한 번 더
        // 발생시킬 수 있다 — Enter/Escape 경로가 이미 처리를 끝냈으면 이 플래그로 중복 커밋(과
        // 중복 RenderTree 호출)을 막는다.
        var settled = false;

        editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                settled = true;
                CommitRename(node, editBox.Text);
            }
            else if (e.Key == Key.Escape)
            {
                settled = true;
                RenderTree(); // 취소 — 값 반영 없이 원래 표시로.
            }
        };
        editBox.LostFocus += (_, _) =>
        {
            if (!settled)
            {
                settled = true;
                CommitRename(node, editBox.Text);
            }
        };

        content.Children[nameIndex] = editBox;
        editBox.Focus();
        editBox.SelectAll();
    }

    /// <summary><paramref name="newName"/>이 공백이 아니면 <paramref name="node"/>.Name에 반영하고, 어느 쪽이든 <see cref="RenderTree"/>로 편집 상태를 정리합니다.</summary>
    private void CommitRename(StructureTreeNode node, string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            node.Name = newName.Trim();
        }

        RenderTree();
    }
}
