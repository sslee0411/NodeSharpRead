using System.Windows;
using NodeSharp.UI.Themes;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 앱 진입점
/// 역활 및 기능 : NodeSharp.Editor(WPF) 시작 시 기본 테마를 적용하는 Application 파생 클래스
///
/// (ED-B0) 앱이 뜨자마자(OnStartup) 기본 테마를 적용합니다 — MainWindow의 컨트롤들은
/// DynamicResource로 테마 키를 참조하므로, 이 호출 이후 화면이 실제로 그려질 때 테마 색상이
/// 적용된 상태로 표시됩니다.
/// (ED-B4) lssLib 실제 테마 시스템 포팅에 맞춰 <see cref="ThemeManager.ApplyTheme"/>(AppTheme.Dark,
/// 자리표시자 2테마)를 <see cref="ThemeManager.ApplyDefault"/>(DarkNavy, 실제 7테마 중 기본값)로
/// 교체했습니다 — 타이틀바의 ThemePickerButton(ED-B3+ED-B4)으로 나머지 6개 테마도 실행 중 전환할
/// 수 있습니다.
/// </summary>
public partial class App : Application
{
    /// <summary>WPF 프레임워크가 앱 시작 시 자동 호출합니다 — 여기서 1회 기본 테마를 적용합니다.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 0) 기본 테마 적용(ED-B0 → ED-B4) — DarkNavy(7테마 중 기본값). 사용자는 타이틀바의
        //    ThemePickerButton으로 실행 중 언제든 다른 테마로 바꿀 수 있다.
        ThemeManager.ApplyDefault();
    }
}
