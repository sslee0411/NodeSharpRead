using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Models;

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
/// </summary>
public partial class FlowCanvasView : UserControl
{
    // EC-05(다중 Flow 탭)가 만들어지기 전까지 모든 노드는 이 고정 FlowId 하나에 속한다(임시).
    private const string DefaultFlowId = "f1";

    private readonly List<NodeConfig> _placedNodes = new();
    private int _nextNodeSeq = 1;

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

        _placedNodes.Add(config);
        RenderNode(config, position);
        Palette.MarkTypeUsed(typeName);
        EmptyCanvasHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// <paramref name="config"/>를 나타내는 작은 카드(Border+TextBlock)를 <paramref name="dropPosition"/>
    /// 중심으로 <c>NodeCanvas</c>에 추가합니다. 포트·와이어 연결점 등 실제 캔버스 표현은 EC-02
    /// 이후에 보강됩니다 — 지금은 "배치됐다"는 사실만 눈으로 확인 가능하면 됩니다.
    /// </summary>
    private void RenderNode(NodeConfig config, Point dropPosition)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = config.Name,
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            }
        };

        Canvas.SetLeft(card, Math.Max(0, dropPosition.X - 40));
        Canvas.SetTop(card, Math.Max(0, dropPosition.Y - 12));
        NodeCanvas.Children.Add(card);
    }
}
