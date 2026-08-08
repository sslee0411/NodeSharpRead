using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Core.Config;
using NodeSharp.Registry;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Flow 캔버스 뷰
/// 역활 및 기능 : MainWindow 본문 좌측에 항상 표시되는 Flow 캔버스 자리(ED-B2a 시점에는 빈 화면)
///
/// (ED-B2a) 02번 문서 8번 탭 카드15가 확정한 "항상 분할 도킹" 설계에 따라, 이 뷰는 화면 전환 없이
/// <see cref="StructureView"/>와 GridSplitter를 사이에 두고 항상 동시에 보입니다. 실제 노드
/// 배치·와이어 연결 UI는 Phase 6(Editor 캔버스)에서 이 UserControl 안에 채워집니다.
/// (EC-01a) 좌측에 <see cref="PaletteView"/>(노드 팔레트)를 추가했습니다 — 실제 캔버스 렌더링·드래그
/// 배치는 다음 Step(EC-01b)에서 우측 자리표시자를 대체합니다.
/// (EC-01b) 우측을 실제 <c>Canvas</c>(<c>NodeCanvas</c>)로 바꾸고, 팔레트에서 시작된 WPF 표준
/// 드래그 앤 드롭을 받아(<see cref="OnCanvasDrop"/>) <see cref="NodeConfig"/>를 생성·화면에 렌더링합니다.
/// (EC-02) 각 카드에 입력/출력 포트(<see cref="Ellipse"/>)를 추가하고, 출력 포트를 누른 채
/// 입력 포트 위에서 놓으면 <see cref="Wire"/>가 생성되는 드래그 상호작용을 구현했습니다 — WPF
/// 표준 드래그 앤 드롭(팔레트 배치용)과는 별개로 <see cref="Mouse.Capture(System.Windows.IInputElement)"/>
/// 기반의 자체 메커니즘을 씁니다. 지금은 <c>NodeTypeRegistry</c>에 등록된 실제 노드 타입이 없어
/// (Phase 7 이전) 모든 카드를 입력 1개·출력 1개로 고정합니다 — 노드 타입별 실제 포트 개수
/// (<c>INodeTypeDescriptor.DefaultInputs</c>/<c>DefaultOutputs</c>) 반영은 Phase 7 이후로 미룹니다.
/// (EC-03) 카드를 더블클릭하면 <see cref="NodePropertyDialog"/>가 뜹니다(<see cref="OpenPropertyDialog"/>) —
/// 이 뷰가 직접 만든 <c>NodeTypeRegistry</c>(팔레트와 별개 인스턴스, EC-01a와 동일한 패턴)에서
/// 해당 타입의 PropertySchema를 찾아 넘기고, "완료"로 닫히면 <see cref="_nodeConfigs"/>와 카드에
/// 표시된 이름을 갱신합니다.
/// (EC-04) <see cref="_flowStore"/>(<see cref="FlowStore"/>)로 flows.json 저장/로드를 붙였습니다.
/// 이 뷰가 로드될 때(<see cref="OnLoaded"/>) 저장된 내용이 있으면 노드·와이어를 그대로 복원하고,
/// <see cref="SaveFlowAsync"/>를 호출하면(<c>MainWindow</c>의 "저장" 메뉴/Ctrl+S) 지금 캔버스 상태를
/// <c>flows.json</c>에 원자적으로 저장합니다. 노드 카드의 캔버스 좌표는 <see cref="NodeConfig.X"/>/
/// <see cref="NodeConfig.Y"/>에 직접 저장하도록 바뀌어(EC-04 신규 필드), <see cref="RenderNode"/>가
/// 별도의 드롭 좌표 매개변수 없이 <c>config.X</c>/<c>config.Y</c>를 그대로 읽습니다.
/// (EC-05, ★ 사용자 요청) 상단 <c>FlowTabStrip</c>에 여러 Flow 탭(<see cref="FlowTabInfo"/>)을
/// 추가/전환/삭제할 수 있습니다. 이전에는 노드 전체가 고정 <c>FlowId</c>("f1") 하나에 속했지만,
/// 이제 <see cref="_nodeConfigs"/>/<see cref="_wires"/>는 <b>모든 탭의 데이터를 함께</b> 보관하고
/// 각 <see cref="NodeConfig.FlowId"/>로 소속 탭을 구분합니다 — 캔버스에는 <see cref="_activeFlowId"/>
/// 탭의 노드·와이어만 그려집니다(<see cref="SwitchToFlow"/>가 <c>NodeCanvas</c>를 비우고 다시
/// 그리는 방식, WPF 요소 show/hide보다 단순함). flows.json은 이제 <see cref="FlowDefinition"/>
/// 목록(탭 개수만큼)이고, <see cref="SaveFlowAsync"/>는 탭별로 자기 소속 노드·와이어만 모아 각각의
/// <see cref="FlowDefinition"/>을 만듭니다. 설계 판단 근거(단일 스키마 → 리스트 스키마 전환,
/// Runner 쪽 <c>StartupSequencer</c>/<c>FlowDeployer</c> 동시 수정)는
/// <c>NodeSharp.Editor.csproj</c>/<c>NodeSharp.Runner.csproj</c>의 EC-05 블록을 참고하십시오.
/// </summary>
public partial class FlowCanvasView : UserControl
{
    // EC-02 시점엔 모든 카드를 이 고정 크기·고정 포트 개수(입력1/출력1)로 그린다(위 클래스 주석 참고).
    private const double NodeCardWidth = 120;
    private const double NodeCardHeight = 40;
    private const double PortRadius = 5;

    // EC-03 PropertySchema 조회 전용 — 팔레트(PaletteView)와는 별개 인스턴스(EC-01a와 동일 패턴).
    private readonly NodeTypeRegistry _registry = new(contractsVersion: "1.0.0");

    // EC-04 flows.json 저장/로드 전용 창구(순수 System.IO 래퍼, 클래스 자체 주석 참고).
    private readonly FlowStore _flowStore = new();
    private bool _flowLoaded;

    // (EC-05) 모든 Flow 탭의 데이터를 함께 담는 전역 딕셔너리 — NodeConfig.FlowId로 소속 탭을 구분한다.
    // 노드 Id는 탭이 달라도 항상 전역적으로 유일해야 한다(Wire.SourceNodeId/TargetNodeId가 탭 구분 없이
    // Id만으로 노드를 참조하고, Runner 배포 시(FlowDeployer)에도 여러 탭의 Nodes가 하나로 병합되므로).
    private readonly Dictionary<string, NodeConfig> _nodeConfigs = new();
    private readonly Dictionary<string, TextBlock> _nodeLabels = new();
    private readonly List<Wire> _wires = new();
    private readonly Dictionary<string, PlacedNodeVisual> _nodeVisuals = new();
    private int _nextNodeSeq = 1;

    // (EC-05) Flow 탭 목록·현재 활성 탭·다음 탭 Id 발급 순번. 최초 상태는 탭 1개("f1", "Flow 1") —
    // LoadFlowAsync가 저장된 flows.json을 찾으면 이 기본 탭을 지우고 불러온 탭들로 교체한다.
    private readonly List<FlowTabInfo> _flowTabs = new() { new FlowTabInfo("f1", "Flow 1") };
    private string _activeFlowId = "f1";
    private int _nextFlowTabSeq = 2;

    // EC-02 와이어 드래그 진행 상태 — 출력 포트를 누르는 순간부터 마우스를 놓을 때까지만 값이 있다.
    private PortHandle? _dragSourcePort;
    private Line? _dragPreviewLine;
    private PortHandle? _hoveredInputPort;

    /// <summary>
    /// (EC-04) flows.json을 읽고 쓸 데이터 폴더 경로. Runner의 <c>Worker.cs</c>가 쓰는 것과 같은
    /// <see cref="AppContext.BaseDirectory"/>(실행 파일이 있는 폴더)를 기본값으로 둡니다 — Editor와
    /// Runner가 실제로 같은 폴더를 공유하도록 배치하는 방법(설정 파일 경로 지정 등)은 이후 Phase 8
    /// (LK-01~04) 범위의 더 깊은 배포 구성 문제라 이 Step에서는 다루지 않습니다.
    /// </summary>
    public string DataDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴). (EC-05) 초기 탭 스트립("Flow 1" 탭
    /// 1개)을 즉시 그립니다 — <see cref="LoadFlowAsync"/>는 <see cref="Loaded"/> 이후 비동기로
    /// 실행되므로, 그 전까지 화면이 비어 보이지 않도록 기본 탭을 먼저 그려둡니다.
    /// </summary>
    public FlowCanvasView()
    {
        InitializeComponent();
        RenderFlowTabStrip();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// (EC-04) 이 뷰가 화면에 처음 나타날 때(WPF <see cref="FrameworkElement.Loaded"/>) 저장된
    /// flows.json이 있으면 <see cref="LoadFlowAsync"/>로 복원합니다. <see cref="_flowLoaded"/>로
    /// 한 번만 시도하도록 막습니다(Loaded는 컨트롤이 시각 트리에서 빠졌다 다시 붙으면 다시 발생할
    /// 수 있는 이벤트라, 이미 복원했다면 다시 실행할 필요가 없음).
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_flowLoaded)
        {
            return;
        }

        _flowLoaded = true;
        await LoadFlowAsync();
    }

    /// <summary>
    /// (EC-04, EC-05 확장) <see cref="DataDirectory"/>\flows.json을 읽어 저장된 Flow 탭 목록이 있으면
    /// 기본 탭("f1", "Flow 1")을 지우고 그 목록으로 완전히 교체합니다. 모든 탭의 노드·와이어를
    /// <see cref="_nodeConfigs"/>/<see cref="_wires"/>에 함께 채운 뒤, 노드 Id("n1", "n2"...)와 탭
    /// Id("f1", "f2"...) 각각 가장 큰 순번 다음 값으로 <see cref="_nextNodeSeq"/>/
    /// <see cref="_nextFlowTabSeq"/>를 재계산해(불러온 것과 겹치지 않도록), 첫 번째 탭으로
    /// <see cref="SwitchToFlow"/>를 호출해 화면을 그립니다.
    /// </summary>
    private async Task LoadFlowAsync()
    {
        var flows = await _flowStore.LoadAsync(DataDirectory);
        if (flows is null || flows.Count == 0)
        {
            return;
        }

        _flowTabs.Clear();
        _nodeConfigs.Clear();
        _wires.Clear();

        foreach (var flow in flows)
        {
            _flowTabs.Add(new FlowTabInfo(flow.Id, flow.Name));
            foreach (var node in flow.Nodes)
            {
                _nodeConfigs[node.Id] = node;
            }

            _wires.AddRange(flow.Wires);
        }

        if (_flowTabs.Count == 0)
        {
            // 이론상 flows.json이 빈 배열([])로 저장된 경우 — 기본 탭 1개로 복구해 빈 목록 상태를 피한다.
            _flowTabs.Add(new FlowTabInfo("f1", "Flow 1"));
        }

        var maxNodeSeq = 0;
        foreach (var node in _nodeConfigs.Values)
        {
            if (node.Id.StartsWith("n", StringComparison.Ordinal) &&
                int.TryParse(node.Id.AsSpan(1), out var seq) && seq > maxNodeSeq)
            {
                maxNodeSeq = seq;
            }
        }

        if (maxNodeSeq > 0)
        {
            _nextNodeSeq = maxNodeSeq + 1;
        }

        var maxTabSeq = 0;
        foreach (var tab in _flowTabs)
        {
            if (tab.Id.StartsWith("f", StringComparison.Ordinal) &&
                int.TryParse(tab.Id.AsSpan(1), out var seq) && seq > maxTabSeq)
            {
                maxTabSeq = seq;
            }
        }

        if (maxTabSeq > 0)
        {
            _nextFlowTabSeq = maxTabSeq + 1;
        }

        SwitchToFlow(_flowTabs[0].Id);
    }

    /// <summary>
    /// (EC-04, EC-05 확장) 지금 메모리에 있는 모든 Flow 탭의 노드·와이어를 탭별로 각각
    /// <c>FlowDefinition</c> 하나씩으로 모아(탭에 속한 노드만 <see cref="NodeConfig.FlowId"/>로 필터링,
    /// 와이어는 양쪽 끝 노드가 모두 그 탭에 속할 때만 포함) 목록으로 만든 뒤
    /// <see cref="DataDirectory"/>\flows.json에 원자적으로 저장합니다(<see cref="FlowStore.SaveAsync"/>).
    /// <c>MainWindow</c>의 "파일 → 저장" 메뉴/Ctrl+S가 이 메서드를 호출합니다.
    /// </summary>
    public async Task SaveFlowAsync()
    {
        var flows = _flowTabs
            .Select(tab => new FlowDefinition(
                tab.Id,
                tab.Name,
                _nodeConfigs.Values.Where(n => n.FlowId == tab.Id).ToList(),
                _wires.Where(w => IsWireInFlow(w, tab.Id)).ToList()))
            .ToList();

        await _flowStore.SaveAsync(flows, DataDirectory);
    }

    /// <summary>
    /// <paramref name="wire"/>의 양쪽 끝 노드가 모두 <paramref name="flowId"/> 탭에 속하는지 확인합니다
    /// (저장 시 와이어를 어느 탭의 <c>FlowDefinition.Wires</c>에 넣을지 판단하는 용도).
    /// </summary>
    private bool IsWireInFlow(Wire wire, string flowId) =>
        _nodeConfigs.TryGetValue(wire.SourceNodeId, out var source) && source.FlowId == flowId &&
        _nodeConfigs.TryGetValue(wire.TargetNodeId, out var target) && target.FlowId == flowId;

    /// <summary>
    /// (EC-05) 새 Flow 탭을 만들고("f{n}", "Flow {n}") 곧바로 그 탭으로 전환합니다. 탭 스트립의
    /// "＋" 버튼이 이 메서드를 호출합니다.
    /// </summary>
    private void AddFlowTab()
    {
        var id = $"f{_nextFlowTabSeq}";
        var name = $"Flow {_nextFlowTabSeq}";
        _nextFlowTabSeq++;

        _flowTabs.Add(new FlowTabInfo(id, name));
        SwitchToFlow(id);
    }

    /// <summary>
    /// (EC-05) <paramref name="flowId"/> 탭을 삭제합니다 — 이 탭에 속한 노드·와이어가 모두 함께
    /// 삭제되므로 사용자에게 먼저 확인을 받습니다. 남은 탭이 1개뿐이면(완료 기준이 "탭 3개 이상을
    /// 추가/전환/삭제해도"라 최소 1개는 항상 있어야 함) 삭제를 거부합니다. 삭제된 탭이 현재 활성
    /// 탭이었으면 남은 탭 중 첫 번째로 전환합니다.
    /// </summary>
    private void RemoveFlowTab(string flowId)
    {
        if (_flowTabs.Count <= 1)
        {
            MessageBox.Show(
                "마지막 남은 Flow 탭은 삭제할 수 없습니다.",
                "탭 삭제 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var tab = _flowTabs.FirstOrDefault(t => t.Id == flowId);
        if (tab is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"'{tab.Name}' 탭을 삭제하시겠습니까? 이 탭에 배치된 노드와 와이어가 모두 함께 삭제됩니다.",
            "Flow 탭 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var removedNodeIds = new HashSet<string>(
            _nodeConfigs.Values.Where(n => n.FlowId == flowId).Select(n => n.Id));
        foreach (var nodeId in removedNodeIds)
        {
            _nodeConfigs.Remove(nodeId);
        }

        _wires.RemoveAll(w => removedNodeIds.Contains(w.SourceNodeId) || removedNodeIds.Contains(w.TargetNodeId));
        _flowTabs.Remove(tab);

        if (_activeFlowId == flowId)
        {
            SwitchToFlow(_flowTabs[0].Id);
        }
        else
        {
            RenderFlowTabStrip();
        }
    }

    /// <summary>
    /// (EC-05) <paramref name="flowId"/> 탭으로 전환합니다 — <c>NodeCanvas</c>의 시각 요소(카드·포트·
    /// 와이어 선)를 전부 지우고(데이터인 <see cref="_nodeConfigs"/>/<see cref="_wires"/>는 그대로
    /// 유지) 그 탭에 속한 노드만 <see cref="RenderNode"/>로, 양쪽 끝이 모두 그 탭에 속한 와이어만
    /// <see cref="DrawWireLine"/>로 다시 그립니다. WPF 요소를 show/hide로 전환하는 대신 매번 새로
    /// 그리는 더 단순한 방식을 택했습니다(탭 전환이 잦은 조작이 아니라 성능 부담이 적음).
    /// </summary>
    private void SwitchToFlow(string flowId)
    {
        _activeFlowId = flowId;

        NodeCanvas.Children.Clear();
        _nodeLabels.Clear();
        _nodeVisuals.Clear();
        _dragSourcePort = null;
        _dragPreviewLine = null;
        _hoveredInputPort = null;

        var tabNodes = _nodeConfigs.Values.Where(n => n.FlowId == flowId).ToList();
        foreach (var node in tabNodes)
        {
            RenderNode(node);
        }

        foreach (var wire in _wires)
        {
            if (IsWireInFlow(wire, flowId))
            {
                var source = new PortHandle(wire.SourceNodeId, wire.SourcePort, IsOutput: true);
                var target = new PortHandle(wire.TargetNodeId, wire.TargetPort, IsOutput: false);
                DrawWireLine(source, target);
            }
        }

        EmptyCanvasHint.Visibility = tabNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderFlowTabStrip();
    }

    /// <summary>
    /// (EC-05) <see cref="_flowTabs"/>를 <c>FlowTabStrip</c>(가로 <see cref="StackPanel"/>)에 탭
    /// 버튼(이름 + "✕" 삭제 아이콘)으로 그리고, 맨 끝에 "＋"(새 탭 추가) 버튼을 붙입니다. 현재
    /// <see cref="_activeFlowId"/>인 탭은 AccentBrush 배경으로 강조합니다. <see cref="RenderNode"/>와
    /// 동일하게 데이터 템플릿 없이 즉석에서 Border+TextBlock을 만드는 코드비하인드 스타일입니다.
    /// </summary>
    private void RenderFlowTabStrip()
    {
        FlowTabStrip.Children.Clear();

        foreach (var tab in _flowTabs)
        {
            var isActive = tab.Id == _activeFlowId;

            var label = new TextBlock
            {
                Text = tab.Name,
                Foreground = (Brush)FindResource(isActive ? "PrimaryTextBrush" : "SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var closeGlyph = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = tab.Id
            };
            closeGlyph.MouseLeftButtonDown += OnFlowTabCloseClick;

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(label);
            content.Children.Add(closeGlyph);

            var tabButton = new Border
            {
                Background = (Brush)FindResource(isActive ? "AccentBrush" : "ControlBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Tag = tab.Id,
                Child = content
            };
            tabButton.MouseLeftButtonDown += OnFlowTabClick;

            FlowTabStrip.Children.Add(tabButton);
        }

        var addButton = new Border
        {
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "＋", Foreground = (Brush)FindResource("PrimaryTextBrush") }
        };
        addButton.MouseLeftButtonDown += (_, _) => AddFlowTab();

        FlowTabStrip.Children.Add(addButton);
    }

    /// <summary>(EC-05) 탭 버튼을 클릭하면 그 탭으로 전환합니다.</summary>
    private void OnFlowTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string flowId })
        {
            SwitchToFlow(flowId);
        }
    }

    /// <summary>
    /// (EC-05) 탭의 "✕" 아이콘을 클릭하면 그 탭을 삭제합니다. <paramref name="e"/>.Handled를
    /// <c>true</c>로 설정해, 이 클릭이 부모 탭 버튼의 <see cref="OnFlowTabClick"/>(탭 전환)으로
    /// 버블링되지 않도록 막습니다(삭제 확인 대화상자가 뜨기 직전에 먼저 그 탭으로 전환돼버리는
    /// 것을 방지).
    /// </summary>
    private void OnFlowTabCloseClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string flowId })
        {
            RemoveFlowTab(flowId);
        }

        e.Handled = true;
    }

    /// <summary>
    /// (EC-01b) 팔레트 카드가 <c>NodeCanvas</c> 위에 드롭되면 문자열 데이터(TypeName)를 꺼내
    /// <see cref="NodeConfig"/>를 새로 만들고(<see cref="_nextNodeSeq"/>로 "n1", "n2"... 순번 Id
    /// 발급) <see cref="RenderNode"/>로 화면에 카드를 그립니다. 팔레트의 "최근 사용"도 함께
    /// 갱신합니다(<see cref="PaletteView.MarkTypeUsed"/>) — 클릭뿐 아니라 실제 배치도 "사용"으로
    /// 인정합니다. (EC-05) 새 노드의 <see cref="NodeConfig.FlowId"/>는 고정값이 아니라 지금
    /// 활성화된 탭(<see cref="_activeFlowId"/>)입니다 — 사용자가 보고 있는 탭에 정확히 배치됩니다.
    /// </summary>
    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.StringFormat) is not string typeName || typeName.Length == 0)
        {
            return;
        }

        var position = e.GetPosition(NodeCanvas);
        var config = new NodeConfig(
            Id: $"n{_nextNodeSeq++}",
            Type: typeName,
            Name: typeName,
            FlowId: _activeFlowId,
            Properties: new Dictionary<string, object?>(),
            X: position.X,
            Y: position.Y);

        _nodeConfigs[config.Id] = config;
        RenderNode(config);
        Palette.MarkTypeUsed(typeName);
        EmptyCanvasHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// <paramref name="config"/>를 나타내는 작은 카드(Border+TextBlock)를 <see cref="NodeConfig.X"/>/
    /// <see cref="NodeConfig.Y"/> 중심으로 <c>NodeCanvas</c>에 추가하고(EC-01b), 좌우에 입력/출력
    /// 포트 Ellipse를 붙입니다(EC-02, <see cref="AddPortEllipse"/>). 카드 크기가 고정이라
    /// (<see cref="NodeCardWidth"/>/<see cref="NodeCardHeight"/>) WPF 레이아웃 측정을 기다리지
    /// 않고도 포트 좌표를 바로 계산할 수 있습니다. (EC-05) 이 메서드는 <paramref name="config"/>.FlowId가
    /// 현재 활성 탭인지 확인하지 않습니다 — 호출부(<see cref="OnCanvasDrop"/>/<see cref="SwitchToFlow"/>)가
    /// 항상 활성 탭에 속한 노드만 넘겨준다는 것을 전제로 합니다.
    /// </summary>
    private void RenderNode(NodeConfig config)
    {
        var left = Math.Max(0, config.X - NodeCardWidth / 2);
        var top = Math.Max(0, config.Y - NodeCardHeight / 2);

        var label = new TextBlock
        {
            Text = config.Name,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4)
        };

        var card = new Border
        {
            Width = NodeCardWidth,
            Height = NodeCardHeight,
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Arrow,
            Tag = config.Id,
            Child = label
        };
        card.MouseLeftButtonDown += OnCardMouseLeftButtonDown;

        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        NodeCanvas.Children.Add(card);
        _nodeLabels[config.Id] = label;

        // EC-02 범위: 지금은 모든 노드를 입력 1개·출력 1개로 고정한다(클래스 주석 참고 — Phase 7
        // 이후 실제 노드 타입의 DefaultInputs/DefaultOutputs를 반영할 예정).
        const int inputs = 1;
        const int outputs = 1;

        var visual = new PlacedNodeVisual(config.Id, left, top, NodeCardWidth, NodeCardHeight, inputs, outputs);
        _nodeVisuals[config.Id] = visual;

        for (var i = 0; i < inputs; i++)
        {
            AddPortEllipse(new PortHandle(config.Id, i, IsOutput: false), visual.GetInputPortPosition(i));
        }

        for (var i = 0; i < outputs; i++)
        {
            AddPortEllipse(new PortHandle(config.Id, i, IsOutput: true), visual.GetOutputPortPosition(i));
        }
    }

    /// <summary>
    /// <paramref name="handle"/>이 나타내는 포트 하나를 <paramref name="center"/> 위치에 작은 원으로
    /// 그립니다. 출력 포트는 <see cref="OnOutputPortMouseDown"/>로 와이어 드래그를 시작하고, 입력
    /// 포트는 <see cref="_hoveredInputPort"/>를 갱신해 "지금 이 포트 위에 마우스가 있다"는 것만
    /// 기억합니다(실제 Wire 생성 판정은 <see cref="OnCanvasMouseUp"/>에서 이 값을 읽어 처리).
    /// </summary>
    private void AddPortEllipse(PortHandle handle, Point center)
    {
        var ellipse = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Fill = (Brush)FindResource("AccentBrush"),
            Tag = handle,
            Cursor = Cursors.Hand
        };

        Canvas.SetLeft(ellipse, center.X - PortRadius);
        Canvas.SetTop(ellipse, center.Y - PortRadius);
        Panel.SetZIndex(ellipse, 1);
        NodeCanvas.Children.Add(ellipse);

        if (handle.IsOutput)
        {
            ellipse.MouseLeftButtonDown += OnOutputPortMouseDown;
        }
        else
        {
            ellipse.MouseEnter += (_, _) => _hoveredInputPort = handle;
            ellipse.MouseLeave += (_, _) =>
            {
                if (Equals(_hoveredInputPort, handle))
                {
                    _hoveredInputPort = null;
                }
            };
        }
    }

    /// <summary>
    /// (EC-02) 출력 포트를 누르면 와이어 드래그를 시작합니다 — 시작점을 기억하고, 미리보기용
    /// <see cref="Line"/>을 캔버스에 추가한 뒤 <see cref="Mouse.Capture(System.Windows.IInputElement)"/>로
    /// 이후 마우스 이벤트를 전부 <c>NodeCanvas</c>가 받도록 만듭니다(포트 Ellipse 밖으로 마우스가
    /// 나가도 드래그가 끊기지 않게 하기 위함).
    /// </summary>
    private void OnOutputPortMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Ellipse { Tag: PortHandle handle })
        {
            return;
        }

        _dragSourcePort = handle;

        var start = _nodeVisuals[handle.NodeId].GetOutputPortPosition(handle.PortIndex);
        _dragPreviewLine = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = start.X,
            Y2 = start.Y,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2
        };
        NodeCanvas.Children.Add(_dragPreviewLine);

        Mouse.Capture(NodeCanvas);
        e.Handled = true;
    }

    /// <summary>와이어 드래그 중이면 미리보기 선의 끝점을 현재 마우스 위치로 계속 갱신합니다.</summary>
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPreviewLine is null)
        {
            return;
        }

        var current = e.GetPosition(NodeCanvas);
        _dragPreviewLine.X2 = current.X;
        _dragPreviewLine.Y2 = current.Y;
    }

    /// <summary>
    /// (EC-02) 마우스를 놓으면 드래그를 끝냅니다. <see cref="_hoveredInputPort"/>가 다른 노드의
    /// 입력 포트를 가리키고 있으면 <see cref="Wire"/>를 만들고 실선으로 그리며, 포트 영역 밖(또는
    /// 자기 자신)에서 놓으면 미리보기 선만 지우고 아무 것도 만들지 않습니다(완료 기준의 "포트
    /// 영역 밖에서 드롭하면 생성되지 않는지" 조건).
    /// </summary>
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragSourcePort is not { } source || _dragPreviewLine is null)
        {
            return;
        }

        NodeCanvas.Children.Remove(_dragPreviewLine);
        _dragPreviewLine = null;
        Mouse.Capture(null);

        if (_hoveredInputPort is { } target && target.NodeId != source.NodeId)
        {
            var wire = new Wire(source.NodeId, source.PortIndex, target.NodeId, target.PortIndex);
            _wires.Add(wire);
            DrawWireLine(source, target);
        }

        _dragSourcePort = null;
    }

    /// <summary><paramref name="source"/>→<paramref name="target"/> 사이에 실선을 그려 완성된 연결을 표시합니다.</summary>
    private void DrawWireLine(PortHandle source, PortHandle target)
    {
        var start = _nodeVisuals[source.NodeId].GetOutputPortPosition(source.PortIndex);
        var end = _nodeVisuals[target.NodeId].GetInputPortPosition(target.PortIndex);

        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = (Brush)FindResource("PrimaryTextBrush"),
            StrokeThickness = 2
        };

        // 새 와이어 선이 나중에 그려진 노드 카드 위를 덮지 않도록 맨 뒤(카드보다 아래)로 보낸다.
        Panel.SetZIndex(line, -1);
        NodeCanvas.Children.Insert(0, line);
    }

    /// <summary>
    /// (EC-03) 카드를 더블클릭(<c>e.ClickCount == 2</c>)하면 그 카드의 Tag(NodeId)로
    /// <see cref="OpenPropertyDialog"/>를 엽니다. 한 번 클릭은 무시합니다(포트 드래그와 헷갈리지
    /// 않도록 카드 자체에는 단일 클릭 동작을 두지 않음).
    /// </summary>
    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { Tag: string nodeId })
        {
            return;
        }

        OpenPropertyDialog(nodeId);
        e.Handled = true;
    }

    /// <summary>
    /// (EC-03) <paramref name="nodeId"/>의 현재 <see cref="NodeConfig"/>와, <see cref="_registry"/>에
    /// 등록된 해당 타입의 PropertySchema(없으면 빈 목록 — Phase 7 이전엔 항상 이 경우)로
    /// <see cref="NodePropertyDialog"/>를 모달로 띄웁니다. "완료"로 닫히면 <see cref="_nodeConfigs"/>와
    /// 카드에 표시된 이름(<see cref="_nodeLabels"/>)을 갱신합니다.
    /// </summary>
    private void OpenPropertyDialog(string nodeId)
    {
        if (!_nodeConfigs.TryGetValue(nodeId, out var config))
        {
            return;
        }

        var schema = _registry.Descriptors.TryGetValue(config.Type, out var descriptor)
            ? descriptor.PropertySchema
            : Array.Empty<PropertyField>();

        var dialog = new NodePropertyDialog(config, schema)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.UpdatedConfig is { } updated)
        {
            _nodeConfigs[nodeId] = updated;
            if (_nodeLabels.TryGetValue(nodeId, out var label))
            {
                label.Text = updated.Name;
            }
        }
    }
}
