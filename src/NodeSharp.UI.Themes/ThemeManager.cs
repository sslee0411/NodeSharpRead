using System.Windows;

namespace NodeSharp.UI.Themes;

/// <summary>
/// Class명 : 테마 관리자
/// 역활 및 기능 : 다크/라이트 ResourceDictionary를 앱의 병합 리소스에 등록·교체하는 정적 클래스
///
/// (ED-B0) NodeSharp.Editor(WPF)가 시작할 때 <see cref="ApplyTheme"/>를 1회 호출해 기본 테마를
/// 적용합니다. Themes/DarkTheme.xaml·Themes/LightTheme.xaml 두 ResourceDictionary는 같은
/// 리소스 키(WindowBackgroundBrush 등)를 다른 색상으로 정의해두어, 화면의 각 컨트롤은
/// StaticResource가 아니라 <c>DynamicResource</c>로 그 키를 참조해야 <see cref="ApplyTheme"/>
/// 호출만으로 실행 중에도 테마가 즉시 바뀝니다(lssLib WPF 테마와 동일한 DynamicResource 원칙).
/// </summary>
/// <remarks>
/// 완료 기준("앱 기동 시 다크/라이트 테마가 적용된 빈 창이 예외 없이 표시되는지 확인")의 실제
/// 확인은 WPF 창이 실제로 화면에 그려지는지를 봐야 해서, 이 개발 환경(Linux 샌드박스, WPF 자체가
/// 실행되지 않음)에서는 자동 검증이 불가능합니다 — RN-03a/RN-07(PowerShell 스크립트, xUnit 대상
/// 아님)과 같은 유형이라 이 Step도 자동 테스트 없이 사용자가 Windows에서 직접 실행해 확인합니다.
/// </remarks>
/// <example>
/// <code>
/// // App.xaml.cs — 시작 시 기본 테마 적용
/// protected override void OnStartup(StartupEventArgs e)
/// {
///     base.OnStartup(e);
///     ThemeManager.ApplyTheme(AppTheme.Dark);
/// }
/// </code>
/// </example>
public static class ThemeManager
{
    private const string DarkThemeUri = "pack://application:,,,/NodeSharp.UI.Themes;component/Themes/DarkTheme.xaml";
    private const string LightThemeUri = "pack://application:,,,/NodeSharp.UI.Themes;component/Themes/LightTheme.xaml";

    /// <summary>
    /// <paramref name="theme"/>에 맞는 ResourceDictionary를 로드해 <c>Application.Current.Resources
    /// .MergedDictionaries</c>에 추가합니다. 이전에 적용된 테마 딕셔너리가 있으면(Dark/Light 둘 중
    /// 하나) 먼저 제거해 중복 등록을 막습니다 — 그래야 실행 중에 다시 호출해도 테마가 깔끔하게
    /// 전환됩니다.
    /// </summary>
    public static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current가 없습니다 — WPF 앱이 시작된 뒤에만 호출할 수 있습니다.");

        var newDictionary = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri)
        };

        var previousThemeDictionary = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source is not null &&
                (d.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.Ordinal) ||
                 d.Source.OriginalString.EndsWith("LightTheme.xaml", StringComparison.Ordinal)));

        if (previousThemeDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(previousThemeDictionary);
        }

        app.Resources.MergedDictionaries.Add(newDictionary);
    }
}
