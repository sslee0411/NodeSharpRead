namespace NodeSharp.UI.Themes;

/// <summary>
/// Class명 : 앱 테마
/// 역활 및 기능 : Editor 화면에 적용할 수 있는 다크/라이트 2가지 테마를 구분하는 값
///
/// (ED-B0) <see cref="ThemeManager.ApplyTheme"/>에 이 값을 넘기면 그에 맞는 ResourceDictionary
/// (Themes/DarkTheme.xaml 또는 Themes/LightTheme.xaml)가 앱 전체에 적용됩니다.
/// </summary>
public enum AppTheme
{
    /// <summary>어두운 배경의 다크 테마 — 이 프로젝트의 기본값(App.xaml.cs에서 시작 시 적용).</summary>
    Dark,

    /// <summary>밝은 배경의 라이트 테마.</summary>
    Light
}
