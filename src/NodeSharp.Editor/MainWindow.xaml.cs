using System.Windows;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 메인 창
/// 역활 및 기능 : NodeSharp.Editor의 최상위 WPF 창(ED-B0 시점에는 테마만 적용된 빈 창)
///
/// (ED-B0) 지금은 MainWindow.xaml의 DynamicResource 바인딩으로 테마 색상만 확인하는 빈 창입니다.
/// 헤더+메뉴+본문 레이아웃(ED-B1), Flow/구조설정 통합 뷰(ED-B2a) 등은 이후 Step에서 이 창 안에
/// 채워집니다.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
