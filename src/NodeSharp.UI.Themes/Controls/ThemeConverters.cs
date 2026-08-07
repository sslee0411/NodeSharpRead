// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · Controls/ThemeConverters.cs
//  역할: ThemePickerButton.xaml 에서 사용하는 XAML 변환기 3종
//
//  ★ 별도 파일로 분리한 이유:
//     WPF 빌드 파이프라인에서 XAML 컴파일러는 C# 컴파일러보다 먼저 실행된다.
//     ThemePickerButton.xaml.cs (코드비하인드) 안에 정의된 보조 클래스는
//     같은 xaml 파일에서 참조할 수 없다.
//     → 반드시 독립적인 .cs 파일에 정의해야 XAML이 정상 인식한다.
//
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\Controls\ThemeConverters.cs
//        (ED-B4, "복사 참조" 포팅 — 네임스페이스만 바꿈, 로직은 원본과 100% 동일)
// ══════════════════════════════════════════════════════════
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
namespace NodeSharp.UI.Themes.Controls;

// ── §1 ─ HexToColorConverter ─────────────────────────────

/// <summary>
/// "#RRGGBB" 또는 "#AARRGGBB" 문자열 → WPF <see cref="Color"/> 변환.
/// <para>변환 실패 시 <see cref="Colors.Gray"/>를 반환한다.</para>
/// </summary>
public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(
        object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { /* 잘못된 형식 — 기본값 반환 */ }
        }
        return Colors.Gray;
    }

    public object ConvertBack(
        object value, Type targetType,
        object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ── §2 ─ ThemePreviewBgConverter ─────────────────────────

/// <summary>
/// <c>IsDark(bool)</c> → 테마 카드 배경 미리보기 <see cref="Color"/> 변환.
/// <list type="bullet">
///   <item>true  → <c>#111520</c> (Dark Navy 배경)</item>
///   <item>false → <c>#EEF1F6</c> (Steel Light 배경)</item>
/// </list>
/// </summary>
public sealed class ThemePreviewBgConverter : IMultiValueConverter
{
    private static readonly Color DarkBg  = Color.FromRgb(0x11, 0x15, 0x20);
    private static readonly Color LightBg = Color.FromRgb(0xEE, 0xF1, 0xF6);

    public object Convert(
        object[] values, Type targetType,
        object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is bool isDark)
            return isDark ? DarkBg : LightBg;
        return DarkBg;
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes,
        object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ── §3 ─ BoolToVisibilityConverter ───────────────────────

/// <summary>
/// <c>bool</c> → <see cref="Visibility"/> 변환.
/// <list type="bullet">
///   <item>true  → <see cref="Visibility.Visible"/></item>
///   <item>false → <see cref="Visibility.Collapsed"/></item>
/// </list>
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value, Type targetType,
        object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(
        object value, Type targetType,
        object parameter, CultureInfo culture)
        => (Visibility)value == Visibility.Visible;
}
