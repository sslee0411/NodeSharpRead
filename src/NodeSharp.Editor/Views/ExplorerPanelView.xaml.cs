using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Explorer 패널 뷰
/// 역활 및 기능 : Ctrl+F로 캔버스에 이미 배치된 노드를 여러 Flow 탭에 걸쳐 검색하고 결과를 보여주는 사이드바
///
/// (EC-12) Node-RED 5.0의 "Explorer" 사이드바(02번 문서 9번 탭 카드16)에 대응합니다. EC-01a의 팔레트
/// 검색(아직 배치하지 않은 노드 종류)과 달리, 이미 캔버스에 놓인 노드를 이름/속성 값 텍스트로
/// 찾습니다. 이 뷰 자신은 <see cref="FlowCanvasView"/>를 직접 참조하지 않고(<see cref="InformationPanelView"/>와
/// 같은 원칙 — 값/이벤트로만 오갑니다) <c>MainWindow</c>가 <see cref="QueryChanged"/>를
/// <c>FlowCanvas.SearchNodes</c>에, <see cref="ResultActivated"/>를 <c>FlowCanvas.NavigateToNode</c>에
/// 연결합니다. <see cref="FocusSearchBox"/>는 Ctrl+F(<c>ApplicationCommands.Find</c>)로 이 탭이
/// 선택된 직후 검색창에 곧바로 포커스를 줘, 사용자가 탭을 클릭할 필요 없이 바로 입력을 시작할 수
/// 있게 합니다.
/// </summary>
public partial class ExplorerPanelView : UserControl
{
    /// <summary>검색창 내용이 바뀔 때마다(글자 하나 입력/삭제할 때마다) 그 즉시 전체 텍스트를 전달합니다.</summary>
    public event Action<string>? QueryChanged;

    /// <summary>검색 결과 하나를 클릭하면 (FlowId, NodeId) 순서로 전달합니다.</summary>
    public event Action<string, string>? ResultActivated;

    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public ExplorerPanelView()
    {
        InitializeComponent();
    }

    /// <summary>검색창에 포커스를 주고 기존 텍스트를 전체 선택합니다 — Ctrl+F 진입 지점(<c>MainWindow</c>가 호출).</summary>
    public void FocusSearchBox()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    /// <summary>
    /// <c>MainWindow</c>가 <c>FlowCanvas.SearchNodes(query)</c>로 얻은 결과를 받아 목록을 다시
    /// 그립니다. 결과가 비어 있으면(검색어가 비어 있거나 일치하는 노드가 없으면) 안내 문구로
    /// 되돌립니다.
    /// </summary>
    public void ShowResults(IReadOnlyList<NodeSearchResult> results)
    {
        ResultsPanel.Children.Clear();

        if (results.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            ResultsScroll.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        ResultsScroll.Visibility = Visibility.Visible;

        foreach (var result in results)
        {
            ResultsPanel.Children.Add(BuildResultRow(result));
        }
    }

    /// <summary>
    /// <paramref name="result"/> 하나를 클릭 가능한 카드(이름 + "Flow 탭 · 타입" 부제목 2줄)로
    /// 만듭니다. 클릭하면 <see cref="ResultActivated"/>를 발생시킵니다.
    /// </summary>
    private Border BuildResultRow(NodeSearchResult result)
    {
        var nameLine = new TextBlock
        {
            Text = result.NodeName,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var subtitleLine = new TextBlock
        {
            Text = $"{result.FlowName} · {result.NodeType}",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 2),
            Background = (Brush)FindResource("WindowBackgroundBrush"),
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
            Child = new StackPanel { Children = { nameLine, subtitleLine } }
        };
        row.MouseLeftButtonDown += (_, _) => ResultActivated?.Invoke(result.FlowId, result.NodeId);

        return row;
    }

    /// <summary>검색창 텍스트가 바뀔 때마다 <see cref="QueryChanged"/>를 그대로 전파합니다.</summary>
    private void OnQueryTextChanged(object sender, TextChangedEventArgs e) => QueryChanged?.Invoke(QueryBox.Text);
}
