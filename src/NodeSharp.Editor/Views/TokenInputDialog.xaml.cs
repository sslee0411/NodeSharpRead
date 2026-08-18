using System.Windows;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 토큰 입력 대화상자
/// 역활 및 기능 : "Runner 토큰 입력" 메뉴가 띄우는 단순 모달 — 사용자가 텍스트 한 줄(runner.token 값)을
/// 입력하면 <see cref="EnteredToken"/>에 담아 돌려주는 최소 구현
///
/// (LK-03) 02번 설계 문서 7번 탭 카드6 "원격 PC는 사용자가 직접 입력"을 실제로 받는 화면입니다.
/// 프로젝트에 아직 범용 텍스트 입력 대화상자가 없어(지금까지는 <see cref="Microsoft.Win32.OpenFileDialog"/>
/// 같은 OS 표준 다이얼로그만 사용 — <c>RunnerProcessManager</c> 참고) 새 NuGet 의존성을 추가하지
/// 않고 <see cref="NodePropertyDialog"/>와 동일한 관례(모달, DialogResult)로 가장 단순한 형태
/// (TextBox 1개 + 확인/취소)로 직접 만들었습니다.
/// </summary>
public partial class TokenInputDialog : Window
{
    /// <summary>사용자가 "확인"을 눌렀을 때 입력한 값 — <see cref="Window.DialogResult"/>가 <c>true</c>일 때만 의미 있습니다.</summary>
    public string EnteredToken { get; private set; } = string.Empty;

    /// <summary>XAML에서 정의한 컨트롤을 초기화합니다(WPF 표준 패턴).</summary>
    public TokenInputDialog()
    {
        InitializeComponent();
    }

    /// <summary>입력값을 <see cref="EnteredToken"/>에 담고 <see cref="Window.DialogResult"/>를 <c>true</c>로 닫습니다.</summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        EnteredToken = TokenTextBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>아무것도 저장하지 않고 <see cref="Window.DialogResult"/>를 <c>false</c>로 닫습니다.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
