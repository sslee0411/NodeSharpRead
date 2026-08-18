using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NodeSharp.Editor.Structure;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 구조 설정 뷰
/// 역활 및 기능 : 장비→PLC→디바이스맵→태그→스케일→알람 6단계 고정 트리(<see cref="StructureTreeNode"/>
/// 기반)를 렌더링하고, 각 단계 노드의 추가/삭제/이름 변경을 처리하는 화면
///
/// (ED-D01) 완료 기준("6단계 트리가 StructureTreeNode로 렌더링되고, 각 단계 노드 추가/삭제가 정상
/// 동작하는지")을 만족합니다. 트리 상태는 이 클래스가 메모리에만 들고 있습니다 — device.json
/// 저장/로드(ED-D03), 실제 속성 편집 폼(ED-D02a/b), TagRef 연동(ED-D04)은 모두 이후 Step 범위입니다.
/// (ED-B2a) 02번 문서 8번 탭 카드15의 "항상 분할 도킹" 설계에 따라 <see cref="FlowCanvasView"/>와
/// GridSplitter를 사이에 두고 항상 동시에 표시됩니다.
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
/// <item><b>이름 변경</b>: 행 이름 텍스트를 더블클릭하거나 컨텍스트 메뉴 "이름 변경"으로 시작합니다.
/// Enter 또는 포커스를 잃으면 커밋되고, 빈 문자열이면 이전 이름을 그대로 유지합니다.</item>
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

    /// <summary>XAML 컨트롤을 초기화하고 "+ 장비" 버튼을 연결한 뒤, 빈 트리를 1회 렌더링합니다(WPF 표준 패턴).</summary>
    public StructureView()
    {
        InitializeComponent();
        AddDeviceButton.MouseLeftButtonDown += (_, _) => AddRoot();
        RenderTree();
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

    /// <summary><see cref="Devices"/> 전체를 <see cref="TreePanel"/>에 다시 그립니다 — 추가/삭제/펼침전환/선택마다 호출되는 단일 갱신 지점(EC-05 "데이터를 바꾸고 한 메서드로 화면을 맞춘다" 원칙과 동일).</summary>
    private void RenderTree()
    {
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
                BeginRename(node);
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

    /// <summary><paramref name="node"/>를 선택 노드로 표시하고 다시 그립니다(선택 배경 갱신).</summary>
    private void Select(StructureTreeNode node)
    {
        _selectedNode = node;
        RenderTree();
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

        var renameItem = new MenuItem { Header = "이름 변경" };
        renameItem.Click += (_, _) => BeginRename(node);
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = "삭제" };
        deleteItem.Click += (_, _) => DeleteNode(node);
        menu.Items.Add(deleteItem);

        return menu;
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
