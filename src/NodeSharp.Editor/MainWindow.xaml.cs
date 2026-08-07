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
