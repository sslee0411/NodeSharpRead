// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · ThemeTransitionService.cs
//  역할: 테마 전환 시 부드러운 페이드 애니메이션
//        메인 윈도우에 오버레이를 씌워 깜빡임 없이 전환
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\ThemeTransitionService.cs
//        (ED-B4, "복사 참조" 포팅 — 네임스페이스만 바꿈, 로직은 원본과 100% 동일)
// ══════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
namespace NodeSharp.UI.Themes;

/// <summary>
/// 테마 전환 시 시각적 전환 애니메이션 제공.
/// 전환 방식: 메인 Grid에 반투명 오버레이 → 테마 교체 → 페이드 아웃.
/// </summary>
public static class ThemeTransitionService
{
    // §1 ─ 전환 설정 ──────────────────────────────────────────
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration HoldDuration = new(TimeSpan.FromMilliseconds(30));

    // §2 ─ 공개 API ───────────────────────────────────────────

    /// <summary>
    /// 페이드 전환 애니메이션과 함께 테마를 교체한다.
    /// <para>
    /// 내부 동작:
    /// ① 오버레이 페이드인 → ② ThemeManager.Apply() → ③ 오버레이 페이드아웃
    /// </para>
    /// </summary>
    /// <param name="targetKind">전환할 테마</param>
    /// <param name="rootPanel">애니메이션을 적용할 루트 Grid/Panel</param>
    /// <param name="app">대상 Application</param>
    public static async Task ApplyWithTransitionAsync(
        ThemeKind targetKind,
        Panel rootPanel,
        Application? app = null)
    {
        if (ThemeManager.Current == targetKind) return;

        // ① 오버레이 생성 + 페이드인
        var overlay = CreateOverlay(rootPanel);
        await FadeInAsync(overlay);

        // ② 테마 교체 (ResourceDictionary 교체 — 즉시 반영)
        ThemeManager.Apply(targetKind, app);

        // ③ 짧은 홀드 후 페이드아웃
        await Task.Delay(HoldDuration.TimeSpan);
        await FadeOutAsync(overlay);

        // ④ 오버레이 제거
        rootPanel.Children.Remove(overlay);
    }

    /// <summary>
    /// 애니메이션 없이 즉시 테마 교체 (단순 래퍼).
    /// ThemeManager.Apply() 직접 호출과 동일하지만 저장이 포함됨.
    /// </summary>
    public static void ApplyImmediate(ThemeKind targetKind, Application? app = null)
        => ThemeManager.Apply(targetKind, app);

    // §3 ─ 내부 헬퍼 ──────────────────────────────────────────

    /// <summary>루트 패널 위에 검정 오버레이 Rectangle 추가</summary>
    private static Rectangle CreateOverlay(Panel root)
    {
        var overlay = new Rectangle
        {
            Fill = Brushes.Black,
            Opacity = 0,
            IsHitTestVisible = false,  // 클릭 통과
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Panel.ZIndex 최상위
        Panel.SetZIndex(overlay, 9999);
        root.Children.Add(overlay);
        return overlay;
    }

    private static Task FadeInAsync(Rectangle overlay)
    {
        var tcs = new TaskCompletionSource<bool>();
        var anim = new DoubleAnimation(0, 1, FadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => tcs.TrySetResult(true);
        overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        return tcs.Task;
    }

    private static Task FadeOutAsync(Rectangle overlay)
    {
        var tcs = new TaskCompletionSource<bool>();
        var anim = new DoubleAnimation(1, 0, FadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => tcs.TrySetResult(true);
        overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        return tcs.Task;
    }
}
