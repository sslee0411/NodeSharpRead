using System.Windows;
using NodeSharp.UI.Themes;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 앱 진입점
/// 역활 및 기능 : NodeSharp.Editor(WPF) 시작 시 기본 테마를 적용하는 Application 파생 클래스
///
/// (ED-B0) 앱이 뜨자마자(OnStartup) <see cref="ThemeManager.ApplyTheme"/>로 다크 테마를 기본
/// 적용합니다 — MainWindow의 컨트롤들은 DynamicResource로 테마 키를 참조하므로, 이 호출 이후
/// 화면이 실제로 그려질 때 다크 테마 색상이 적용된 상태로 표시됩니다.
/// </summary>
public partial class App : Application
{
    /// <summary>WPF 프레임워크가 앱 시작 시 자동 호출합니다 — 여기서 1회 기본 테마를 적용합니다.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 0) 기본 테마 적용(ED-B0) — 지금은 다크 고정, 사용자가 테마를 고르는 설정 화면은 이후 Step 범위.
        ThemeManager.ApplyTheme(AppTheme.Dark);
    }
}
