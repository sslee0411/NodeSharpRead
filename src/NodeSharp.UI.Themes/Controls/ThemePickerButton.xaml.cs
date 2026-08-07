// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · Controls/ThemePickerButton.xaml.cs
//  역할: ThemePickerButton UserControl 코드비하인드
//
//  설계 원칙:
//    · DataContext = ThemePickerViewModel (생성자 주입)
//    · Unloaded 이벤트 → ViewModel.Dispose() 로 정적 이벤트 구독 해제
//    · Popup.StaysOpen=False → 외부 클릭 시 WPF 자동 닫힘
//      (별도 MouseDown 후킹 불필요)
//
//  ★ 컨버터 클래스(HexToColorConverter 등)는 ThemeConverters.cs 에 정의.
//     코드비하인드 보조 클래스는 XAML 컴파일러가 인식 못 함.
//
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\Controls\ThemePickerButton.xaml.cs
//        (ED-B4, "복사 참조" 포팅 — 네임스페이스와 예시 XML 주석의 assembly명만 바꿈)
// ══════════════════════════════════════════════════════════
using System.Windows.Controls;
namespace NodeSharp.UI.Themes.Controls;

/// <summary>
/// Class명 : 테마 선택 팝업 버튼
/// 역활 및 기능 : 현재 테마를 표시하고 클릭 시 팝업 드롭다운으로 8가지 테마를 선택할 수 있는
/// 컴팩트 UserControl
///
/// (ED-B4) lssLib IIoT.UI.Themes의 ThemePickerButton을 그대로 포팅했습니다. 사용 예시(다른 WPF
/// 화면에서):
/// <code>
/// xmlns:uc="clr-namespace:NodeSharp.UI.Themes.Controls;assembly=NodeSharp.UI.Themes"
/// ...
/// &lt;uc:ThemePickerButton/&gt;
/// </code>
/// 테마 전환은 내부적으로 <see cref="ThemeManager.Apply"/>를 호출하므로 외부에서 별도로 처리할
/// 필요가 없습니다.
/// </summary>
public partial class ThemePickerButton : UserControl
{
    private readonly ThemePickerViewModel _vm;

    public ThemePickerButton()
    {
        InitializeComponent();

        _vm = new ThemePickerViewModel();
        DataContext = _vm;

        // ★ 정적 이벤트 구독 해제 — 반드시 호출
        Unloaded += (_, _) => _vm.Dispose();
    }
}
