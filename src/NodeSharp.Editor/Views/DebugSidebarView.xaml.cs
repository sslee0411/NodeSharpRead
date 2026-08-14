using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Events;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Debug 사이드바 뷰
/// 역활 및 기능 : Runner가 SignalR로 보낸 DebugMessageEvent/NodeErrorEvent를 시간순으로 쌓아 보여주는 읽기 전용 패널
///
/// (LK-02b) Node-RED Editor의 "Debug" 사이드바 탭(EC-11이 Information/Explorer 탭을 붙일 때 이미
/// "필요하면 탭만 늘리면 됨"이라고 예상해둔 자리, MainWindow.xaml EC-11 블록 참고)에 대응합니다.
/// <see cref="InformationPanelView"/>/<see cref="ExplorerPanelView"/>와 동일하게 <see cref="FlowCanvasView"/>나
/// <c>EditorMonitorClient</c>(Core, LK-02b)를 직접 참조하지 않고 값만 전달받는 단방향 구조입니다 —
/// <c>MainWindow</c>가 <c>EditorMonitorClient.DebugMessageReceived</c>/<c>NodeErrorReceived</c>를 이
/// 뷰의 <see cref="AppendDebugMessage"/>/<see cref="AppendNodeError"/>에 연결합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>일시정지</b>: <see cref="IsPaused"/>가 true인 동안 들어오는 이벤트는 목록에 추가하지
/// 않고 그냥 버립니다(Node-RED 원본처럼 버퍼링했다가 재개 시 한꺼번에 쏟아내지 않음) — "흐름을
/// 잠깐 멈추고 지금까지 쌓인 것만 천천히 읽고 싶다"는 목적에는 버림만으로 충분하다고 판단해,
/// 버퍼링 큐까지는 이 Step 범위 밖으로 둡니다.</item>
/// <item><b>최대 개수 제한</b>: <see cref="MaxEntries"/>(200)를 넘으면 가장 오래된 항목부터 지웁니다 —
/// 장시간 켜두면 무한히 쌓여 WPF <see cref="StackPanel"/>이 느려지는 것을 막기 위함
/// (<c>NodeSharp.Editor.Core.Commands.CommandHistory</c>의 "최대 50단계" 제한과 동일한 정신).</item>
/// <item><b>네이티브 Button 대신 Border+Click</b>: XAML 파일 자체 주석 참고 — 이 프로젝트 테마
/// (NodeSharp.UI.Themes)의 키 있는 Button 스타일(GhostBtn 등, Styles.Controls.xaml)은 실제
/// Theme.*.xaml이 정의하는 브러시 키(PrimaryTextBrush 등)와 이름 체계가 달라(AccBrush/Text2Brush/
/// CardBrush 등) 어디서도 참조되지 않는 미사용 상태라 그대로 쓰면 리소스를 못 찾을 위험이 있습니다.
/// <c>FlowCanvasView.RenderFlowTabStrip</c>의 "＋"/"✕"와 동일하게 <see cref="Border"/> +
/// <c>MouseLeftButtonDown</c>으로 안전하게 구현했습니다.</item>
/// </list>
/// </remarks>
public partial class DebugSidebarView : UserControl
{
    private const int MaxEntries = 200;

    /// <summary>true면 새 이벤트를 목록에 추가하지 않고 버립니다(위 클래스 remarks "일시정지" 항목).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>XAML에서 정의한 컨트롤들을 초기화하고 일시정지/지우기 버튼 클릭을 연결합니다(WPF 표준 패턴).</summary>
    public DebugSidebarView()
    {
        InitializeComponent();
        PauseButton.MouseLeftButtonDown += (_, _) => TogglePause();
        ClearButton.MouseLeftButtonDown += (_, _) => Clear();
    }

    /// <summary><see cref="IsPaused"/>를 뒤집고 버튼 라벨/배경을 갱신합니다.</summary>
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        PauseButtonLabel.Text = IsPaused ? "재개" : "일시정지";
        PauseButton.Background = IsPaused ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
    }

    /// <summary>쌓인 항목을 전부 지웁니다(일시정지 여부와 무관하게 항상 가능).</summary>
    private void Clear()
    {
        ContentPanel.Children.Clear();
        RefreshEmptyHint();
    }

    /// <summary>Runner가 보낸 <see cref="DebugMessageEvent"/> 하나를 목록 맨 위에 추가합니다(일시정지 중이면 무시).</summary>
    public void AppendDebugMessage(DebugMessageEvent evt)
    {
        if (IsPaused)
        {
            return;
        }

        AddEntry($"{evt.At:HH:mm:ss} · {evt.NodeName}", evt.MsgJson, isError: false);
    }

    /// <summary>
    /// Runner가 보낸 <see cref="NodeErrorEvent"/> 하나를 목록 맨 위에 추가합니다(일시정지 중이면 무시,
    /// <see cref="NodeErrorEvent.StackTrace"/>가 있으면 본문 아래에 함께 표시).
    /// </summary>
    public void AppendNodeError(NodeErrorEvent evt)
    {
        if (IsPaused)
        {
            return;
        }

        var body = string.IsNullOrWhiteSpace(evt.StackTrace)
            ? evt.Message
            : $"{evt.Message}\n{evt.StackTrace}";
        AddEntry($"{evt.At:HH:mm:ss} · {evt.NodeId} (오류)", body, isError: true);
    }

    /// <summary>제목 한 줄 + 본문 텍스트로 된 항목 하나를 목록 맨 위에 추가하고, <see cref="MaxEntries"/>를 넘으면 가장 오래된 항목을 지웁니다.</summary>
    private void AddEntry(string title, string body, bool isError)
    {
        var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        entry.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource(isError ? "RedBrush" : "SecondaryTextBrush"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        });
        entry.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = (Brush)FindResource(isError ? "RedBrush" : "PrimaryTextBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        ContentPanel.Children.Insert(0, entry);

        while (ContentPanel.Children.Count > MaxEntries)
        {
            ContentPanel.Children.RemoveAt(ContentPanel.Children.Count - 1);
        }

        RefreshEmptyHint();
    }

    /// <summary>목록이 비어 있으면 안내 문구를, 아니면 목록 자체를 보여줍니다(<see cref="InformationPanelView"/>와 동일한 관례).</summary>
    private void RefreshEmptyHint()
    {
        var hasEntries = ContentPanel.Children.Count > 0;
        EmptyHint.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
        ContentScroll.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
    }
}
