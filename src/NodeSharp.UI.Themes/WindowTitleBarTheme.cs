// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · WindowTitleBarTheme.cs
//  역할: Windows DWM API로 OS 네이티브 제목표시줄(캡션) 색상을 현재 ThemeManager 테마에 맞춰 칠함
//  (EC-16, ★ 사용자 요청) 사용자가 NodePropertyDialog 스크린샷으로 "콤보박스 테마는 적용됐는데
//  팝업 창의 타이틀바는 아직 OS 기본(흰색) 그대로"임을 보고 — WPF Window의 기본 제목표시줄은
//  OS(DWM)가 직접 그리는 네이티브 영역이라 DynamicResource/Background 같은 WPF 리소스로는 전혀
//  손댈 수 없다는 것을 확인. MainWindow는 이미 ED-B3에서 WindowStyle="None"+커스텀 타이틀바로
//  이 문제를 우회했지만, NodePropertyDialog는 EC-03 설계 당시 "모달 다이얼로그는 OS 기본 창 틀로도
//  목업이 요구하는 모습과 다르지 않다"고 판단해 의도적으로 커스텀 타이틀바를 적용하지 않았다(파일
//  상단 XAML 주석 참고) — 지금 이 요청 때문에 그 판단 자체를 뒤집을 필요는 없다고 판단(개발 지침
//  5번 저위험 예외) — "OS 기본 창 틀 유지"는 그대로 두고, 그 네이티브 캡션의 "색상"만 DWM API
//  (DwmSetWindowAttribute)로 현재 테마에 맞춰 칠하는 절충안을 택했다. Windows 11(빌드 22000+)은
//  DWMWA_CAPTION_COLOR/DWMWA_TEXT_COLOR로 테마의 실제 색상(예: NeonCyber의 형광색 텍스트)까지
//  정확히 재현하고, 이 두 속성이 없는 Windows 10(1809+)에서는 DWMWA_USE_IMMERSIVE_DARK_MODE로
//  최소한 다크/라이트 여부만이라도 맞춘다(두 호출 모두 실패해도 예외를 삼켜 무시 — 더 오래된 Windows
//  버전에서는 그냥 항상 OS 기본 흰 타이틀바로 남을 뿐, 앱 동작에는 영향 없음).
// ══════════════════════════════════════════════════════════
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NodeSharp.UI.Themes;

/// <summary>
/// Class명 : 창 제목표시줄 테마 적용기
/// 역활 및 기능 : Windows DWM API(DwmSetWindowAttribute)로 OS 네이티브 제목표시줄의 배경·글자·테두리
/// 색상을 현재 <see cref="ThemeManager"/> 테마에 맞춰 칠하는 정적 헬퍼
///
/// WPF의 <see cref="Window.Background"/>·<c>DynamicResource</c>는 창의 <b>내용(클라이언트 영역)</b>만
/// 칠할 뿐, <c>WindowStyle="SingleBorderWindow"</c>(기본값)로 남겨둔 창의 <b>네이티브 제목표시줄</b>은
/// OS(DWM, Desktop Window Manager)가 직접 그리는 영역이라 전혀 영향을 주지 못합니다 — 이 프로젝트의
/// <see cref="NodePropertyDialog"/> 같은 모달 다이얼로그가 다크 테마에서도 제목표시줄만 흰색으로
/// 남는 이유입니다. <see cref="Apply"/>는 <c>dwmapi.dll</c>의 <c>DwmSetWindowAttribute</c>를 직접
/// P/Invoke로 호출해 이 네이티브 영역의 색상만 테마에 맞춥니다(창 자체의 <c>WindowStyle</c>은 그대로
/// 유지 — <see cref="MainWindow"/>처럼 완전한 커스텀 타이틀바로 바꾸는 것과는 다른, 더 가벼운 절충안).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>호출 시점</b>: 반드시 창의 Win32 핸들(HWND)이 생성된 <b>이후</b>에 호출해야 합니다 —
/// <see cref="Window.SourceInitialized"/> 이벤트(또는 그 이후)에서 호출하세요. 그보다 먼저 호출하면
/// <see cref="WindowInteropHelper.Handle"/>이 <see cref="IntPtr.Zero"/>라 조용히 아무 일도 하지
/// 않습니다.</item>
/// <item><b>실행 중 테마 전환 대응</b>: 창이 열려 있는 동안 사용자가 다른 테마로 전환하면 제목표시줄도
/// 함께 갱신되어야 하므로, 호출자가 <see cref="ThemeManager.ThemeChanged"/>를 구독해 <see cref="Apply"/>를
/// 다시 호출해야 합니다(이 클래스 자체는 정적 이벤트를 구독하지 않습니다 — 구독·해제 책임을 호출자에게
/// 남겨, <see cref="ThemePickerViewModel"/>과 동일하게 창이 닫힐 때 반드시 구독 해제하도록 강제하는
/// 것이 이 프로젝트의 메모리 누수 방지 관례, 공통 규칙 ②).</item>
/// <item><b>Windows 버전별 지원 범위</b>: <c>DWMWA_CAPTION_COLOR</c>(35)·<c>DWMWA_TEXT_COLOR</c>(36)·
/// <c>DWMWA_BORDER_COLOR</c>(34)는 Windows 11(빌드 22000+)부터만 지원됩니다 — 이 세 속성으로 테마의
/// 실제 색상(배경/글자/테두리)을 그대로 재현합니다. Windows 10(1809+)은 이 세 속성 호출이 실패하지만
/// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>(20)는 지원해, 최소한 다크/라이트 여부만이라도 반영됩니다.
/// 그보다 오래된 Windows(1809 미만)는 네 호출 모두 실패하며, 이 클래스는 모든 <c>DwmSetWindowAttribute</c>
/// 호출을 개별적으로 try/catch로 감싸 실패를 조용히 무시합니다 — 제목표시줄 색상은 순수 시각적 개선
/// 사항이라, 지원하지 않는 환경에서 예외로 창 표시 자체를 막아서는 안 되기 때문입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public NodePropertyDialog(NodeConfig config, IReadOnlyList&lt;PropertyField&gt; schema)
/// {
///     InitializeComponent();
///     SourceInitialized += (_, _) =&gt; WindowTitleBarTheme.Apply(this);
///     ThemeManager.ThemeChanged += _ =&gt; WindowTitleBarTheme.Apply(this);
///     // ... Closed 이벤트에서 위 람다를 저장해둔 변수로 구독 해제 ...
/// }
/// </code>
/// </example>
public static class WindowTitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

    /// <summary>
    /// <paramref name="window"/>의 네이티브 제목표시줄을 현재 <see cref="ThemeManager"/> 테마
    /// (<c>BgColor</c>/<c>TextColor</c>/<c>BorderColor</c> 리소스와 <see cref="ThemeManager.IsDarkMode"/>)
    /// 색상에 맞춰 칠합니다. 핸들이 아직 없으면(<see cref="Window.SourceInitialized"/> 이전) 아무 일도
    /// 하지 않고 조용히 반환하고, Windows 버전이 특정 속성을 지원하지 않으면 그 속성만 조용히 건너뜁니다
    /// (위 클래스 remarks 참고 — 지원 범위는 Windows 버전에 따라 다름).
    /// </summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var isDark = ThemeManager.IsDarkMode ? 1 : 0;
        TrySetAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref isDark);

        if (TryGetThemeColor(window, "BgColor", out var bg))
        {
            var bgRef = ToColorRef(bg);
            TrySetAttribute(hwnd, DwmwaCaptionColor, ref bgRef);
        }

        if (TryGetThemeColor(window, "TextColor", out var text))
        {
            var textRef = ToColorRef(text);
            TrySetAttribute(hwnd, DwmwaTextColor, ref textRef);
        }

        if (TryGetThemeColor(window, "BorderColor", out var border))
        {
            var borderRef = ToColorRef(border);
            TrySetAttribute(hwnd, DwmwaBorderColor, ref borderRef);
        }
    }

    /// <summary>bool(int) 값을 받는 <c>DwmSetWindowAttribute</c> 오버로드 호출 — 실패(구버전 Windows 등)는 조용히 무시합니다.</summary>
    private static void TrySetAttribute(IntPtr hwnd, int attribute, ref int value)
    {
        try
        {
            DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch
        {
            // 위 클래스 remarks 참고 — 시각적 개선 사항이라 미지원 환경에서도 창 표시를 막지 않는다.
        }
    }

    /// <summary>COLORREF(0x00BBGGRR) 값을 받는 <c>DwmSetWindowAttribute</c> 오버로드 호출 — 실패(Windows 11 미만 등)는 조용히 무시합니다.</summary>
    private static void TrySetAttribute(IntPtr hwnd, int attribute, ref uint value)
    {
        try
        {
            DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(uint));
        }
        catch
        {
            // 위 클래스 remarks 참고 — 시각적 개선 사항이라 미지원 환경에서도 창 표시를 막지 않는다.
        }
    }

    /// <summary><paramref name="window"/>의 현재 테마 리소스에서 <paramref name="key"/> Color 값을 찾습니다(테마 딕셔너리에 없으면 false).</summary>
    private static bool TryGetThemeColor(Window window, string key, out Color color)
    {
        if (window.TryFindResource(key) is Color found)
        {
            color = found;
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>WPF <see cref="Color"/>(ARGB)를 Win32 COLORREF(0x00BBGGRR, 알파 없음)로 변환합니다.</summary>
    private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));
}
