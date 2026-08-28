using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.UI.Themes;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 아이콘 선택 대화상자
/// 역활 및 기능 : 번들된 Font Awesome 아이콘 세트(<see cref="FontAwesomeIconCatalog"/>)에서
/// 검색·클릭으로 아이콘을 고르는 모달
///
/// (EC-20, ★ 사용자 요청 — "아이콘의 경우 웹에 공유되어 있는 아이콘을 선택해서 할 수 있도록")
/// EC-19까지는 NodePropertyDialog의 "아이콘" 필드가 평범한 TextBox라 이모지 등 문자를 직접
/// 타이핑해야 했습니다 — 이 창은 <see cref="FontAwesomeIconCatalog.Icons"/> 51종을 그리드로
/// 보여주고(<see cref="FontAwesomeIconCatalog.IconEntry.Name"/>/<see cref="FontAwesomeIconCatalog.IconEntry.Label"/>
/// 양쪽으로 검색 지원), 클릭하면 그 아이콘의 글리프 문자를 <see cref="SelectedGlyph"/>에 담아
/// 즉시 창을 닫습니다(다른 피커와 달리 "확인" 버튼이 따로 없음 — 클릭 = 선택 확정, 아이콘 그리드류
/// UI의 일반적인 관례). 이모지를 직접 타이핑하던 기존 방식(EC-19)도 계속 지원해야 하므로,
/// NodePropertyDialog의 TextBox는 그대로 남겨두고 이 창은 "선택" 보조 버튼으로만 연결됩니다
/// (<see cref="NodePropertyDialog.CreateInputControl"/>의 <c>Icon</c> 케이스 참고).
/// <see cref="ColorPickerDialog"/>/<see cref="TokenInputDialog"/>와 동일한 최소 모달 관례와
/// <see cref="WindowTitleBarTheme"/>/<see cref="ThemeManager.ThemeChanged"/> 구독·해제 패턴(EC-16)을
/// 그대로 따릅니다.
/// </summary>
public partial class IconPickerDialog : Window
{
    /// <summary>
    /// (EC-16과 동일한 관례) <see cref="ThemeManager.ThemeChanged"/> 구독 해제용으로 기억해두는
    /// 델리게이트 — 정적 이벤트라 해제하지 않으면 이 창이 닫혀도 GC되지 않습니다.
    /// </summary>
    private readonly Action<ThemeKind> _themeChangedHandler;

    /// <summary>사용자가 아이콘을 클릭했을 때의 선택값(글리프 1글자 문자열) — <see cref="Window.DialogResult"/>가 <c>true</c>일 때만 의미 있습니다.</summary>
    public string SelectedGlyph { get; private set; } = string.Empty;

    /// <summary>전체 아이콘 목록으로 그리드를 채웁니다.</summary>
    public IconPickerDialog()
    {
        InitializeComponent();

        // (EC-16과 동일한 이유) SourceInitialized 시점에 제목표시줄을 1회 칠하고, 이 창이 열려 있는
        // 동안 사용자가 테마를 바꾸면 다시 칠하도록 구독한다 — OnClosed에서 반드시 해제.
        SourceInitialized += (_, _) => WindowTitleBarTheme.Apply(this);
        _themeChangedHandler = _ => WindowTitleBarTheme.Apply(this);
        ThemeManager.ThemeChanged += _themeChangedHandler;

        RenderIcons(FontAwesomeIconCatalog.Icons);
    }

    /// <summary>(EC-16과 동일) 창이 닫힐 때 <see cref="ThemeManager.ThemeChanged"/> 구독을 반드시 해제합니다(위 필드 문서 참고).</summary>
    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= _themeChangedHandler;
        base.OnClosed(e);
    }

    /// <summary>
    /// <c>SearchBox</c> 텍스트가 바뀔 때마다 <see cref="FontAwesomeIconCatalog.Icons"/>를
    /// Name/Label 양쪽으로 필터링(대소문자 무시)해 다시 그립니다 — 검색어가 비어 있으면 전체
    /// 목록을 그대로 보여줍니다.
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(keyword)
            ? FontAwesomeIconCatalog.Icons
            : FontAwesomeIconCatalog.Icons
                .Where(icon => icon.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                               icon.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        RenderIcons(filtered);
    }

    /// <summary>
    /// <paramref name="icons"/> 목록으로 <c>IconPanel</c>을 다시 채웁니다(기존 버튼은 모두 지우고
    /// 새로 그림). 각 버튼은 <see cref="FontAwesomeIconCatalog.FontFamily"/>로 글리프를 그리고,
    /// 툴팁으로 한글 설명(<see cref="FontAwesomeIconCatalog.IconEntry.Label"/>)을 보여줍니다.
    /// </summary>
    private void RenderIcons(IReadOnlyList<FontAwesomeIconCatalog.IconEntry> icons)
    {
        IconPanel.Children.Clear();

        if (icons.Count == 0)
        {
            IconPanel.Children.Add(new TextBlock
            {
                Text = "검색 결과가 없습니다.",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var icon in icons)
        {
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(3),
                Tag = icon.Glyph,
                ToolTip = icon.Label,
                Content = new TextBlock
                {
                    Text = icon.Glyph,
                    FontFamily = FontAwesomeIconCatalog.FontFamily,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            button.Click += OnIconClick;
            IconPanel.Children.Add(button);
        }
    }

    /// <summary>
    /// 아이콘 버튼을 클릭하면 그 글리프를 <see cref="SelectedGlyph"/>에 담고 <see cref="Window.DialogResult"/>를
    /// <c>true</c>로 즉시 닫습니다(클릭 = 선택 확정, 위 클래스 주석 참고).
    /// </summary>
    private void OnIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string glyph })
        {
            SelectedGlyph = glyph;
            DialogResult = true;
        }
    }

    /// <summary>"취소" — 아무것도 저장하지 않고 <see cref="Window.DialogResult"/>를 <c>false</c>로 닫습니다.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
