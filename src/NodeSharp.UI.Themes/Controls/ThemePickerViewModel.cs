// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · Controls/ThemePickerViewModel.cs
//  역할: ThemePickerButton 전용 ViewModel
//        - 팝업 열림/닫힘 상태 관리
//        - 현재 테마 표시 텍스트 / 액센트 컬러 제공
//        - ThemeManager 와 동기화 (정적 이벤트 구독)
//
//  IDisposable 구현 필수:
//    ThemeManager.ThemeChanged 는 정적 이벤트이므로
//    구독 해제 없이 ViewModel 이 GC 되지 않는다.
//    UserControl Unloaded 이벤트에서 반드시 Dispose() 호출.
//
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\Controls\ThemePickerViewModel.cs
//        (ED-B4, "복사 참조" 포팅 — 네임스페이스만 바꿈, 로직은 원본과 100% 동일)
//        CommunityToolkit.Mvvm 패키지가 필요해 NodeSharp.UI.Themes.csproj에 참조 추가.
// ══════════════════════════════════════════════════════════
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace NodeSharp.UI.Themes.Controls;

/// <summary>
/// ThemePickerButton 의 ViewModel.
/// 팝업 열림 상태 + 테마 목록 + 현재 테마 관련 표시값을 제공한다.
/// </summary>
public sealed partial class ThemePickerViewModel : ObservableObject, IDisposable
{
    // §1 ─ 팝업 상태 ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArrowGlyph))]
    private bool _isOpen;

    /// <summary>드롭다운 화살표 글리프 — 열림: ▲ / 닫힘: ▼</summary>
    public string ArrowGlyph => IsOpen ? "▲" : "▼";

    // §2 ─ 테마 목록 ─────────────────────────────────────────

    public IReadOnlyList<ThemePickerItemViewModel> Themes { get; }
        = ThemeManager.AllThemes
            .Select(t => new ThemePickerItemViewModel(t))
            .ToList();

    // §3 ─ 현재 테마 표시값 ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDisplayName))]
    [NotifyPropertyChangedFor(nameof(CurrentAccentHex))]
    private ThemePickerItemViewModel? _currentTheme;

    /// <summary>현재 테마 이름 (트리거 버튼에 표시)</summary>
    public string CurrentDisplayName =>
        CurrentTheme?.DisplayName ?? "테마 선택";

    /// <summary>현재 테마 액센트 Hex 색상 (트리거 버튼 도트에 사용)</summary>
    public string CurrentAccentHex =>
        CurrentTheme?.AccentHex ?? "#4F7CFF";

    // §4 ─ 이벤트 구독 ────────────────────────────────────────

    private bool _disposed;
    private readonly Action<ThemeKind> _themeChangedHandler;

    // §5 ─ 생성자 ─────────────────────────────────────────────

    public ThemePickerViewModel()
    {
        _currentTheme = Themes.FirstOrDefault(t => t.Kind == ThemeManager.Current);
        _themeChangedHandler = OnThemeChanged;
        ThemeManager.ThemeChanged += _themeChangedHandler;
    }

    // §6 ─ 명령 ───────────────────────────────────────────────

    /// <summary>팝업 열기/닫기 토글</summary>
    [RelayCommand]
    private void TogglePopup() => IsOpen = !IsOpen;

    /// <summary>팝업 강제 닫기 (닫기 버튼, 외부 ClosePopup 호출용)</summary>
    [RelayCommand]
    public void ClosePopup() => IsOpen = false;

    /// <summary>테마 선택 → ThemeManager 적용 → 팝업 닫기</summary>
    [RelayCommand]
    private void SelectTheme(ThemePickerItemViewModel? item)
    {
        if (item == null) return;
        ThemeManager.Apply(item.Kind);
        // OnThemeChanged 가 CurrentTheme 및 선택 상태 갱신
        IsOpen = false;
    }

    // §7 ─ 외부 테마 변경 동기화 ──────────────────────────────

    private void OnThemeChanged(ThemeKind kind)
    {
        CurrentTheme = Themes.FirstOrDefault(t => t.Kind == kind);
        foreach (var item in Themes)
            item.UpdateSelected(kind);
    }

    // §8 ─ IDisposable ────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        ThemeManager.ThemeChanged -= _themeChangedHandler;
        _disposed = true;
    }
}

// ── 개별 테마 항목 ──────────────────────────────────────────

/// <summary>
/// 팝업 목록 한 행에 해당하는 테마 항목 ViewModel.
/// <para>
/// <see cref="IsNotSelected"/> 프로퍼티를 제공하여 XAML 에서
/// BoolToVisibilityConverter 를 역전시키지 않아도 미선택 상태 Visibility 제어 가능.
/// </para>
/// </summary>
public sealed partial class ThemePickerItemViewModel : ObservableObject
{
    private readonly ThemeInfo _info;

    public ThemeKind Kind        => _info.Kind;
    public string DisplayName    => _info.DisplayName;
    public string Description    => _info.Description;
    public bool   IsDark         => _info.IsDark;

    /// <summary>Dark / Light 라벨 텍스트</summary>
    public string ModeLabel      => IsDark ? "Dark" : "Light";

    /// <summary>액센트 색상 Hex 문자열 (#RRGGBB)</summary>
    public string AccentHex      =>
        $"#{_info.AccentColor.R:X2}{_info.AccentColor.G:X2}{_info.AccentColor.B:X2}";

    // 선택 상태 (Observable)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSelected))]
    private bool _isSelected;

    /// <summary>
    /// IsSelected 의 반전값.
    /// XAML 에서 BoolToVisibilityConverter 를 파라미터 없이 사용할 때
    /// 미선택 상태의 빈 공간 Border Visibility 를 제어하기 위해 사용.
    /// </summary>
    public bool IsNotSelected => !IsSelected;

    public ThemePickerItemViewModel(ThemeInfo info)
    {
        _info       = info;
        _isSelected = info.Kind == ThemeManager.Current;
    }

    /// <summary>현재 적용된 ThemeKind 에 맞춰 선택 상태를 갱신한다.</summary>
    public void UpdateSelected(ThemeKind current)
        => IsSelected = Kind == current;
}
