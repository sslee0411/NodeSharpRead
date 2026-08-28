using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.UI.Themes;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 색상 팔레트 선택 대화상자
/// 역활 및 기능 : 카드 테두리 색을 16진수 직접 입력 대신 클릭 한 번으로 고를 수 있게 하는 스와치 팔레트 모달
///
/// (EC-20, ★ 사용자 요청 — "카드 색상은 색상파레트 에서 선택해서 할수 있도록") EC-19까지는
/// NodePropertyDialog의 "카드 색상" 필드가 평범한 TextBox라 사용자가 매번 #RRGGBB 16진 코드를
/// 외우거나 다른 곳에서 복사해와야 했습니다 — 이 창은 <see cref="NodeCategoryStyle.Catalog"/>의
/// 카테고리 기본 테두리색(EC-20에서 private→public으로 열어 재사용, 값 중복을 피함)과 별도로 준비한
/// 범용 팔레트(<see cref="GeneralPalette"/>)를 스와치 그리드로 보여주고, 클릭하면 그 색의 #RRGGBB
/// 문자열을 <see cref="HexBox"/>에 채웁니다. 직접 16진수를 아는 사용자를 위해 수동 입력(+실시간
/// 미리보기 사각형)도 함께 두어 두 방식을 모두 지원합니다 — "확인"을 누른 시점의 HexBox 값이
/// <see cref="SelectedHex"/>에 담깁니다(팔레트 클릭 후에도 TextBox를 직접 수정해 미세 조정 가능).
/// <see cref="NodePropertyDialog.CreateInputControl"/>의 <c>Color</c> 케이스가 이 다이얼로그를 여는
/// 흐름은 그 클래스 EC-20 항목 참고. <see cref="TokenInputDialog"/>와 동일한 최소 모달 관례(커스텀
/// 타이틀바 없음, WindowStartupLocation="CenterOwner", ResizeMode="NoResize")를 따르고, 이미 테마가
/// 적용된 NodePropertyDialog 안에서 열리므로 그 클래스의 EC-16 패턴과 동일하게
/// <see cref="WindowTitleBarTheme.Apply"/>/<see cref="ThemeManager.ThemeChanged"/> 구독·해제도
/// 그대로 적용합니다.
/// </summary>
public partial class ColorPickerDialog : Window
{
    /// <summary>
    /// (EC-20) 카테고리 기본색과 겹치지 않는 범용 강조색 팔레트입니다 — 흔히 쓰이는 웹 강조색 위주로
    /// 골랐습니다(특정 표준을 참조한 것은 아니고, 이 세션에서 시각적으로 서로 구분되는 16색을
    /// 직접 선정했습니다).
    /// </summary>
    private static readonly string[] GeneralPalette =
    {
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16", "#22C55E",
        "#10B981", "#14B8A6", "#06B6D4", "#3B82F6", "#6366F1", "#8B5CF6",
        "#A855F7", "#D946EF", "#EC4899", "#64748B",
    };

    /// <summary>
    /// (EC-16과 동일한 관례) <see cref="ThemeManager.ThemeChanged"/> 구독 해제용으로 기억해두는
    /// 델리게이트 — 정적 이벤트라 해제하지 않으면 이 창이 닫혀도 GC되지 않습니다.
    /// </summary>
    private readonly Action<ThemeKind> _themeChangedHandler;

    /// <summary>사용자가 "확인"을 눌렀을 때의 선택값(#RRGGBB) — <see cref="Window.DialogResult"/>가 <c>true</c>일 때만 의미 있습니다.</summary>
    public string SelectedHex { get; private set; } = string.Empty;

    /// <summary><paramref name="initialHex"/>(비어있거나 파싱 불가하면 무시)로 미리보기를 채우고 스와치 그리드를 만듭니다.</summary>
    public ColorPickerDialog(string? initialHex)
    {
        InitializeComponent();

        // (EC-16과 동일한 이유) SourceInitialized 시점에 제목표시줄을 1회 칠하고, 이 창이 열려 있는
        // 동안 사용자가 테마를 바꾸면 다시 칠하도록 구독한다 — OnClosed에서 반드시 해제.
        SourceInitialized += (_, _) => WindowTitleBarTheme.Apply(this);
        _themeChangedHandler = _ => WindowTitleBarTheme.Apply(this);
        ThemeManager.ThemeChanged += _themeChangedHandler;

        HexBox.Text = initialHex ?? string.Empty;
        UpdatePreview();
        BuildSwatches();
    }

    /// <summary>(EC-16과 동일) 창이 닫힐 때 <see cref="ThemeManager.ThemeChanged"/> 구독을 반드시 해제합니다(위 필드 문서 참고).</summary>
    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= _themeChangedHandler;
        base.OnClosed(e);
    }

    /// <summary>
    /// <see cref="NodeCategoryStyle.Catalog"/>의 카테고리별 테두리색과 <see cref="GeneralPalette"/>를
    /// 이어붙여 스와치 버튼을 만들어 <c>SwatchPanel</c>에 채웁니다 — 값을 이 클래스에 따로 하드코딩해
    /// 두 곳의 카테고리 색상이 어긋나는 위험을 만들지 않기 위해 Catalog를 직접 읽습니다.
    /// </summary>
    private void BuildSwatches()
    {
        foreach (var (_, style) in NodeCategoryStyle.Catalog)
        {
            AddSwatch(ToHex(style.Border));
        }

        foreach (var hex in GeneralPalette)
        {
            AddSwatch(hex);
        }
    }

    /// <summary><paramref name="hex"/> 색으로 채운 정사각형 스와치 버튼 하나를 <c>SwatchPanel</c>에 추가합니다.</summary>
    private void AddSwatch(string hex)
    {
        var swatch = new Button
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(3),
            Background = (Brush)new BrushConverter().ConvertFromString(hex)!,
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Tag = hex,
            ToolTip = hex
        };
        swatch.Click += OnSwatchClick;
        SwatchPanel.Children.Add(swatch);
    }

    /// <summary>스와치를 클릭하면 그 색의 #RRGGBB 값을 <c>HexBox</c>에 채우고 미리보기를 갱신합니다.</summary>
    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            HexBox.Text = hex;
            UpdatePreview();
        }
    }

    /// <summary><c>HexBox</c> 값이 바뀔 때마다 미리보기 사각형(<c>PreviewBox</c>)을 갱신합니다.</summary>
    private void OnHexBoxTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    /// <summary>
    /// <c>HexBox</c>의 현재 텍스트를 색으로 파싱해 <c>PreviewBox</c>에 반영합니다 — 타이핑 중
    /// 불완전한 값(예: "#3B")은 <see cref="ColorConverter"/>가 예외를 던지므로 조용히 무시하고
    /// 이전 미리보기를 그대로 유지합니다(방어적 파싱, <see cref="FlowCanvasView.ReadColorOverride"/>와
    /// 동일한 이유).
    /// </summary>
    private void UpdatePreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(HexBox.Text) &&
                ColorConverter.ConvertFromString(HexBox.Text) is Color color)
            {
                PreviewBox.Background = new SolidColorBrush(color);
            }
        }
        catch (Exception)
        {
            // 방어적 파싱 — 위 요약 참고.
        }
    }

    /// <summary><paramref name="color"/>를 #RRGGBB 문자열로 변환합니다(알파 채널은 사용하지 않음).</summary>
    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>"확인" — <c>HexBox</c>의 현재 값을 <see cref="SelectedHex"/>에 담고 <see cref="Window.DialogResult"/>를 <c>true</c>로 닫습니다.</summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        SelectedHex = HexBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>"취소" — 아무것도 저장하지 않고 <see cref="Window.DialogResult"/>를 <c>false</c>로 닫습니다.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
