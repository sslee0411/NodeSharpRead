using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NodeSharp.Contracts.Models;
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
/// 아직 <c>EC-05</c>(다중 Flow 탭)가 없어 <see cref="DefaultFlowId"/>로 단일 Flow만 가정하고,
/// 아직 <c>EC-04</c>(flows.json 저장/로드)가 없어 <see cref="_placedNodes"/>는 메모리에만 쌓입니다.
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
/// </summary>
public partial class FlowCanvasView : UserControl
{
    // EC-05(다중 Flow 탭)가 만들어지기 전까지 모든 노드는 이 고정 FlowId 하나에 속한다(임시).
    private const string DefaultFlowId = "f1";

    // EC-02 시점엔 모든 카드를 이 고정 크기·고정 포트 개수(입력1/출력1)로 그린다(위 클래스 주석 참고).
    private const double NodeCardWidth = 120;
    private const double NodeCardHeight = 40;
    private const double PortRadius = 5;

    // EC-03 PropertySchema 조회 전용 — 팔레트(PaletteView)와는 별개 인스턴스(EC-01a와 동일 패턴).
    private readonly NodeTypeRegistry _registry = new(contractsVersion: "1.0.0");

    private readonly Dictionary<string, NodeConfig> _nodeConfigs = new();
    private readonly Dictionary<string, TextBlock> _nodeLabels = new();
    private readonly List<Wire> _wires = new();
    private readonly Dictionary<string, PlacedNodeVisual> _nodeVisuals = new();
    private int _nextNodeSeq = 1;

    // EC-02 와이어 드래그 진행 상태 — 출력 포트를 누르는 순간부터 마우스를 놓을 때까지만 값이 있다.
    private PortHandle? _dragSourcePort;
    private Line? _dragPreviewLine;
    private PortHandle? _hoveredInputPort;

    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public FlowCanvasView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// (EC-01b) 팔레트 카드가 <c>NodeCanvas</c> 위에 드롭되면 문자열 데이터(TypeName)를 꺼내
    /// <see cref="NodeConfig"/>를 새로 만들고(<see cref="_nextNodeSeq"/>로 "n1", "n2"... 순번 Id
    /// 발급) <see cref="RenderNode"/>로 화면에 카드를 그립니다. 팔레트의 "최근 사용"도 함께
    /// 갱신합니다(<see cref="PaletteView.MarkTypeUsed"/>) — 클릭뿐 아니라 실제 배치도 "사용"으로
    /// 인정합니다.
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
            FlowId: DefaultFlowId,
            Properties: new Dictionary<string, object?>());

        _nodeConfigs[config.Id] = config;
        RenderNode(config, position);
        Palette.MarkTypeUsed(typeName);
        EmptyCanvasHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// <paramref name="config"/>를 나타내는 작은 카드(Border+TextBlock)를 <paramref name="dropPosition"/>
    /// 중심으로 <c>NodeCanvas</c>에 추가하고(EC-01b), 좌우에 입력/출력 포트 Ellipse를 붙입니다
    /// (EC-02, <see cref="AddPortEllipse"/>). 카드 크기가 고정이라(<see cref="NodeCardWidth"/>/
    /// <see cref="NodeCardHeight"/>) WPF 레이아웃 측정을 기다리지 않고도 포트 좌표를 바로 계산할
    /// 수 있습니다.
    /// </summary>
    private void RenderNode(NodeConfig config, Point dropPosition)
    {
        var left = Math.Max(0, dropPosition.X - NodeCardWidth / 2);
        var top = Math.Max(0, dropPosition.Y - NodeCardHeight / 2);

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
