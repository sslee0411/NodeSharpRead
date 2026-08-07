// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · ThemeManager.cs
//  역할: 런타임 WPF 테마 전환 관리자 (7가지 테마)
//        기본 테마: DarkNavy
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\ThemeManager.cs
//        (ED-B4, 사용자 요청으로 "복사 참조" 포팅 — 네임스페이스(IIoT.UI.Themes →
//        NodeSharp.UI.Themes)와 pack:// URI의 어셈블리 이름만 이 프로젝트에 맞춰 바꿈,
//        나머지 로직/구조는 원본과 100% 동일)
//
//  (ED-B0) 당시 자리표시자였던 AppTheme(Dark/Light 2값)·ThemeManager.ApplyTheme(AppTheme)를
//  이 파일이 완전히 대체한다 — AppTheme.cs는 더 이상 사용되지 않는다는 안내만 남기고 폐기됨
//  (이 개발 환경에서는 파일 삭제가 허용되지 않아 내용만 비웠다, AppTheme.cs 주석 참고).
// ══════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Media;
namespace NodeSharp.UI.Themes;

/// <summary>사용 가능한 7가지 테마 + NoTheme. 기본값: DarkNavy</summary>
public enum ThemeKind
{
    NoTheme       = -1, // ⓪ 무적용 — Windows 시스템 기본 (비교용)
    DarkNavy      = 0,  // ① 어두운 우주 제어실 (기본)
    SteelLight    = 1,  // ② 밝고 정밀한 산업용
    NeonCyber     = 2,  // ③ 사이버펑크 네온 글로우
    WarmAmber     = 3,  // ④ 황금 앰버 아날로그
    ArcticFrost   = 4,  // ⑤ 북유럽 아이스 블루
    TerminalGreen = 5,  // ⑥ CRT 레트로 터미널
    CarbonElite   = 6,  // ⑦ 탄소섬유 골드 럭셔리
}

/// <summary>테마 메타 정보 (UI 선택 화면 표시용)</summary>
public sealed record ThemeInfo(
    ThemeKind Kind,
    string DisplayName,
    string Description,
    Color AccentColor,
    bool IsDark);

/// <summary>WPF 런타임 테마 전환 관리자</summary>
public static class ThemeManager
{
    // §1 ─ 상수 ──────────────────────────────────────────────
    private const string AssemblyName = "NodeSharp.UI.Themes";

    // §2 ─ 테마 URI 매핑 ─────────────────────────────────────
    private static readonly Dictionary<ThemeKind, string> ThemeUris = new()
    {
        [ThemeKind.NoTheme]       = Pack("Themes/Theme.NoTheme.xaml"),
        [ThemeKind.DarkNavy]      = Pack("Themes/Theme.DarkNavy.xaml"),
        [ThemeKind.SteelLight]    = Pack("Themes/Theme.SteelLight.xaml"),
        [ThemeKind.NeonCyber]     = Pack("Themes/Theme.NeonCyber.xaml"),
        [ThemeKind.WarmAmber]     = Pack("Themes/Theme.WarmAmber.xaml"),
        [ThemeKind.ArcticFrost]   = Pack("Themes/Theme.ArcticFrost.xaml"),
        [ThemeKind.TerminalGreen] = Pack("Themes/Theme.TerminalGreen.xaml"),
        [ThemeKind.CarbonElite]   = Pack("Themes/Theme.CarbonElite.xaml"),
    };

    // §3 ─ 공유 스타일 URI ─────────────────────────────────────
    private static readonly string[] StyleUris =
    [
        Pack("Styles/Styles.Controls.xaml"),
        Pack("Styles/Styles.Layout.xaml"),
        Pack("Styles/Styles.TreeView.xaml"),
    ];

    // §4 ─ 테마 메타 정보 ─────────────────────────────────────
    public static readonly IReadOnlyList<ThemeInfo> AllThemes =
    [
        new(ThemeKind.NoTheme,       "No Theme",       "시스템 기본 (비교용)",             Color.FromRgb(0x00,0x78,0xD4), IsDark: false),
        new(ThemeKind.DarkNavy,      "Dark Navy",      "어두운 우주 제어실 (기본)",        Color.FromRgb(0x4F,0x7C,0xFF), IsDark: true),
        new(ThemeKind.SteelLight,    "Steel Light",    "밝고 정밀한 산업용 (기업 SaaS)",   Color.FromRgb(0x00,0x57,0xD8), IsDark: false),
        new(ThemeKind.NeonCyber,     "Neon Cyber",     "사이버펑크 네온 글로우",           Color.FromRgb(0x00,0xFF,0xA3), IsDark: true),
        new(ThemeKind.WarmAmber,     "Warm Amber",     "황금 앰버 아날로그 정유공장",      Color.FromRgb(0xF5,0x9E,0x0B), IsDark: true),
        new(ThemeKind.ArcticFrost,   "Arctic Frost",   "북유럽 아이스 블루 프리미엄",     Color.FromRgb(0x02,0x84,0xC7), IsDark: false),
        new(ThemeKind.TerminalGreen, "Terminal Green", "CRT 레트로 터미널 1980s",         Color.FromRgb(0x00,0xFF,0x41), IsDark: true),
        new(ThemeKind.CarbonElite,   "Carbon Elite",   "탄소섬유 골드 럭셔리",            Color.FromRgb(0xC9,0xA8,0x4C), IsDark: true),
    ];

    // §5 ─ 상태 ──────────────────────────────────────────────
    private static ThemeKind _current = ThemeKind.DarkNavy;

    /// <summary>현재 적용된 테마 (기본: DarkNavy)</summary>
    public static ThemeKind Current => _current;

    /// <summary>현재 테마가 다크 모드인지</summary>
    public static bool IsDarkMode => AllThemes.First(t => t.Kind == _current).IsDark;

    /// <summary>테마 변경 이벤트</summary>
    public static event Action<ThemeKind>? ThemeChanged;

    // §6 ─ Apply ─────────────────────────────────────────────

    /// <summary>
    /// 지정한 테마를 Application.Resources에 적용한다.
    /// 기본값은 DarkNavy이다.
    /// </summary>
    public static void Apply(ThemeKind theme = ThemeKind.DarkNavy, Application? app = null)
    {
        app ??= Application.Current
            ?? throw new InvalidOperationException("WPF Application이 초기화되지 않았습니다.");

        var merged = app.Resources.MergedDictionaries;

        // 기존 우리 리소스 제거
        foreach (var rd in merged.Where(IsOurDictionary).ToList())
            merged.Remove(rd);

        // ① 테마 색상 먼저 로드
        if (!ThemeUris.TryGetValue(theme, out var themeUri))
            throw new ArgumentOutOfRangeException(nameof(theme));

        merged.Add(new ResourceDictionary { Source = new Uri(themeUri) });

        // ② 공유 스타일 순서대로 로드
        foreach (var styleUri in StyleUris)
            merged.Add(new ResourceDictionary { Source = new Uri(styleUri) });

        _current = theme;
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>DarkNavy 기본 테마 적용 — App.xaml.cs OnStartup()에서 호출</summary>
    public static void ApplyDefault(Application? app = null)
        => Apply(ThemeKind.DarkNavy, app);

    // §7 ─ 헬퍼 메서드 ───────────────────────────────────────

    /// <summary>단일 색상 오버라이드 (고객사 브랜드 색상 등)</summary>
    public static void OverrideColor(string resourceKey, object value, Application? app = null)
    {
        (app ?? Application.Current)!.Resources[resourceKey] = value;
    }

    public static ThemeInfo GetInfo(ThemeKind kind) => AllThemes.First(t => t.Kind == kind);
    public static ThemeInfo GetCurrentInfo() => GetInfo(_current);
    public static T? GetResource<T>(string key) where T : class
        => Application.Current?.Resources[key] as T;
    public static Color GetAccentColor() => GetCurrentInfo().AccentColor;

    // §8 ─ 내부 헬퍼 ─────────────────────────────────────────
    private static string Pack(string path)
        => $"pack://application:,,,/{AssemblyName};component/{path}";

    private static bool IsOurDictionary(ResourceDictionary rd)
        => rd.Source?.ToString().Contains(AssemblyName) ?? false;
}
