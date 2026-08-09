using System.Windows;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 메인 창
/// 역활 및 기능 : NodeSharp.Editor의 최상위 WPF 창(ED-B0 시점에는 테마만 적용된 빈 창)
///
/// (ED-B0) 지금은 MainWindow.xaml의 DynamicResource 바인딩으로 테마 색상만 확인하는 빈 창입니다.
/// 헤더+메뉴+본문 레이아웃(ED-B1), Flow/구조설정 통합 뷰(ED-B2a) 등은 이후 Step에서 이 창 안에
/// 채워집니다.
/// (ED-B2b) Sequence Editor·Dashboard 진입점(헤더 "보기" 메뉴 + 좌측 네비게이션)을 클릭하면
/// 안내 메시지를 띄우는 두 핸들러가 추가됐습니다 — 실제 창은 Phase 10/11에서 만들어집니다.
/// (ED-B3) OS 기본 제목표시줄을 없애고(WindowStyle="None"+WindowChrome) 커스텀 타이틀바를 직접
/// 그렸습니다 — 최소화/최대화-복원/닫기 버튼 3개의 클릭 핸들러(<see cref="OnMinimizeClick"/>/
/// <see cref="OnMaximizeRestoreClick"/>/<see cref="OnCloseClick"/>)와, 최대화 시 작업표시줄을
/// 가리는 WindowChrome 특유의 문제를 방지하는 <see cref="OnWindowStateChanged"/>가 추가됐습니다.
/// (EC-04) "파일 → 저장" 메뉴와 Ctrl+S(Window.InputBindings/CommandBindings, ApplicationCommands.Save)
/// 가 공유하는 <see cref="OnSaveFlowClick"/>가 추가됐습니다 — 캔버스(<c>FlowCanvas</c>, x:Name)의
/// <c>SaveFlowAsync()</c>를 호출해 flows.json에 저장합니다.
/// (EC-06) 같은 패턴으로 "편집 → 복사"/Ctrl+C가 공유하는 <see cref="OnCopyNodeClick"/>과
/// "편집 → 붙여넣기"/Ctrl+V가 공유하는 <see cref="OnPasteNodeClick"/>이 추가됐습니다 — 각각
/// <c>FlowCanvas.CopySelectedNode()</c>/<c>FlowCanvas.PasteNode()</c>를 호출합니다.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// (ED-B2b) 헤더 "보기 → Sequence Editor" 메뉴와 좌측 네비게이션 "Sequence" 항목이 공유하는
    /// 클릭 핸들러입니다. 실제 Sequence Editor 창(11번 탭 카드6, 캔버스와 별개의 독립 Window)은
    /// Phase 10에서 만들어지므로, 지금은 안내 메시지만 띄웁니다. <paramref name="e"/>는
    /// <see cref="RoutedEventArgs"/>를 받는 메서드가 <c>MouseButtonEventHandler</c>(
    /// <see cref="System.Windows.Input.MouseButtonEventArgs"/> 파생)에도 그대로 연결될 수 있게
    /// 하는 C# 델리게이트 반공변성을 이용해, 메뉴 Click과 네비게이션 MouseLeftButtonUp 두 이벤트를
    /// 같은 메서드 하나로 처리합니다.
    /// </summary>
    private void OnSequenceEditorEntryClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Sequence Editor는 Phase 10(Sequence)에서 별도 창으로 제공될 예정입니다.",
            "Sequence Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// (ED-B2b) 헤더 "보기 → Dashboard" 메뉴와 좌측 네비게이션 "Dashboard" 항목이 공유하는 클릭
    /// 핸들러입니다. 실제 Dashboard 화면(9번 탭, 웹+WPF 듀얼 렌더링)은 Phase 11에서 만들어지므로,
    /// 지금은 안내 메시지만 띄웁니다.
    /// </summary>
    private void OnDashboardEntryClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Dashboard는 Phase 11(Dashboard)에서 별도 창으로 제공될 예정입니다.",
            "Dashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>(ED-B3) 커스텀 타이틀바의 최소화 버튼 — 창을 최소화합니다.</summary>
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>(ED-B3) 커스텀 타이틀바의 최대화/복원 버튼 — 현재 상태의 반대로 전환합니다.</summary>
    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>(ED-B3) 커스텀 타이틀바의 닫기 버튼 — 창을 닫습니다(표준 <see cref="Window.Close"/>).</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// (EC-04) "파일 → 저장" 메뉴 Click과 Ctrl+S(CommandBinding.Executed, <see cref="ExecutedRoutedEventArgs"/>도
    /// <see cref="RoutedEventArgs"/> 파생이라 같은 델리게이트 반공변성 기법으로 한 메서드가 두 이벤트를
    /// 모두 처리)이 공유하는 저장 핸들러입니다. <c>FlowCanvas.SaveFlowAsync()</c>를 호출해 지금
    /// 캔버스 상태를 flows.json에 원자적으로 저장하고, 성공/실패를 안내 메시지로 알립니다.
    /// </summary>
    private async void OnSaveFlowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await FlowCanvas.SaveFlowAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"flows.json 저장 중 오류가 발생했습니다.\n{ex.Message}",
                "저장 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// (EC-06) "편집 → 복사" 메뉴 Click과 Ctrl+C(CommandBinding.Executed)가 공유하는 핸들러입니다.
    /// <c>FlowCanvas.CopySelectedNode()</c>를 호출해 지금 캔버스에서 선택된 노드를 내부 클립보드에
    /// 담습니다(선택된 노드가 없으면 <see cref="Views.FlowCanvasView.CopySelectedNode"/> 내부에서
    /// 아무 동작도 하지 않고 조용히 반환합니다).
    /// </summary>
    private void OnCopyNodeClick(object sender, RoutedEventArgs e) => FlowCanvas.CopySelectedNode();

    /// <summary>
    /// (EC-06) "편집 → 붙여넣기" 메뉴 Click과 Ctrl+V(CommandBinding.Executed)가 공유하는
    /// 핸들러입니다. <c>FlowCanvas.PasteNode()</c>를 호출해 내부 클립보드에 담긴 노드를 새 Id로
    /// 재발급해 지금 활성 Flow 탭에 붙여넣습니다(복사한 적이 없으면
    /// <see cref="Views.FlowCanvasView.PasteNode"/> 내부에서 아무 동작도 하지 않습니다).
    /// </summary>
    private void OnPasteNodeClick(object sender, RoutedEventArgs e) => FlowCanvas.PasteNode();

    /// <summary>
    /// (ED-B3) 창 상태가 바뀔 때마다 최대화/복원 버튼 아이콘을 맞는 모양으로 바꾸고, 최대화 상태일
    /// 때는 <see cref="SystemParameters.WorkArea"/>(작업표시줄을 제외한 화면 영역) 크기로 최대
    /// 크기를 제한합니다 — <c>WindowStyle="None"</c>+<c>WindowChrome</c> 조합에서 최대화하면 창이
    /// 작업표시줄까지 덮어버리는 잘 알려진 WPF 문제를 인터롭 없이 간단히 방지하는 방법입니다.
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";

        if (WindowState == WindowState.Maximized)
        {
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
        }
    }
}
