using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Structure;
using NodeSharp.UI.Themes;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 노드 속성 편집 다이얼로그
/// 역활 및 기능 : PropertySchema(PropertyField 목록)를 읽어 입력 폼을 자동으로 그리는 모달 창
///
/// (EC-03) <see cref="NodeConfig"/> 하나와 그 타입의 PropertySchema를 받아, 필드 타입별로 다른
/// 입력 컨트롤(TextBox/PasswordBox/CheckBox/ComboBox 등, <see cref="CreateInputControl"/>)을 자동
/// 생성하고, 모든 필드 아래에 HelpText를 항상 보여줍니다(Example이 있으면 그것도 함께, 개발 지침
/// 4번). "완료"를 누르면 입력값을 모아 새 <see cref="NodeConfig"/>를 만들어(record 불변 —
/// NodeConfig 자체 문서의 "내용을 바꾸려면 항상 새 인스턴스로 교체" 원칙을 그대로 따름)
/// <see cref="UpdatedConfig"/>에 담고 <see cref="Window.DialogResult"/>를 true로 닫습니다. "취소"를
/// 누르면 아무 것도 만들지 않고 닫습니다. TagRef는 ComboBox로 렌더링하되, (ED-D04) 이제
/// <see cref="TagCatalog.CurrentTags"/>(구조 설정 트리가 항상 최신으로 갱신해두는 정적 스냅샷)를
/// 실제 선택지로 채웁니다 — 화면에는 사람이 읽는 "장비/PLC/디바이스맵/태그" 경로가 보이지만, 실제
/// 저장되는 값은 항상 그 태그의 고유 Id입니다(<see cref="CreateInputControl"/>/<see cref="ReadValue"/>
/// 참고, 태그 이름이 바뀌어도 연동이 끊기지 않는 이유).
/// (EC-11) 이름 바로 아래에 "설명"(<see cref="NodeConfig.Description"/>) 입력란도 고정 필드로
/// 함께 제공합니다 — 값이 있으면 캔버스 카드에 문서 배지가 표시되고 클릭 시 팝업으로 이 텍스트를
/// 그대로 보여줍니다.
/// (EC-16, ★ 사용자 요청) 이 창은 EC-03 설계 당시 판단대로 OS 기본 제목표시줄(WindowStyle 기본값)을
/// 그대로 쓰지만, 사용자가 스크린샷으로 "팝업 창의 타이틀바는 아직 OS 기본(흰색) 그대로"임을 보고해
/// <see cref="WindowTitleBarTheme.Apply"/>(DWM API)로 그 네이티브 제목표시줄의 색상만 현재 테마에
/// 맞춥니다 — 창을 커스텀 타이틀바로 바꾸는 EC-03 판단 자체를 뒤집지는 않습니다(자세한 배경은
/// <see cref="WindowTitleBarTheme"/> 클래스 문서 참고).
/// (EC-20, ★ 사용자 요청 — "카드 색상은 색상파레트 에서 선택", "아이콘의 경우 웹에 공유되어 있는
/// 아이콘을 선택") <see cref="PropertyFieldType.Color"/>/<see cref="PropertyFieldType.Icon"/> 필드는
/// <see cref="CreatePickerControl"/>이 만드는 "TextBox + 선택... 버튼" 합성 컨트롤(<see cref="Grid"/>)로
/// 렌더링됩니다 — 버튼을 누르면 각각 <see cref="ColorPickerDialog"/>/<see cref="IconPickerDialog"/>를
/// 모달로 열어 고른 값을 TextBox에 채우고, TextBox는 그대로 직접 수정도 가능하게 남겨둡니다(EC-19
/// 방식의 수동 입력과 EC-20의 팔레트 선택 둘 다 지원). <see cref="ReadValue"/>는 이 합성 컨트롤의
/// <see cref="FrameworkElement.Tag"/>에 담긴 내부 TextBox에서 최종 값을 꺼냅니다.
/// </summary>
public partial class NodePropertyDialog : Window
{
    private readonly NodeConfig _config;
    private readonly IReadOnlyList<PropertyField> _schema;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();

    /// <summary>
    /// (FN-03) <see cref="PropertyField.Key"/> → 그 필드의 라벨+입력 컨트롤+HelpText를 담은
    /// <see cref="StackPanel"/> 행 — <see cref="ApplyConditionalVisibility"/>가 <see cref="PropertyField.VisibleWhenKey"/>가
    /// 있는 필드의 행 전체(라벨까지 포함)를 감추거나 보이는 데 씁니다.
    /// </summary>
    private readonly Dictionary<string, StackPanel> _fieldRows = new();

    /// <summary>
    /// (EC-16) <see cref="ThemeManager.ThemeChanged"/> 구독 해제용으로 기억해두는 델리게이트 —
    /// <see cref="ThemePickerViewModel"/>과 동일한 관례(핸들러를 필드에 저장해뒀다가
    /// <see cref="OnClosed"/>에서 정확히 그 델리게이트로 구독을 해제)로, 정적 이벤트라 해제하지
    /// 않으면 이 창이 닫혀도 GC되지 않습니다(공통 규칙 ②).
    /// </summary>
    private readonly Action<ThemeKind> _themeChangedHandler;

    /// <summary>취소 없이 "완료"로 닫혔을 때, 입력값이 반영된 새 NodeConfig입니다. 취소 시 null입니다.</summary>
    public NodeConfig? UpdatedConfig { get; private set; }

    /// <summary><paramref name="config"/>의 현재 값으로 폼을 채우고, <paramref name="schema"/>에 따라 입력 필드를 그립니다.</summary>
    public NodePropertyDialog(NodeConfig config, IReadOnlyList<PropertyField> schema)
    {
        InitializeComponent();

        _config = config;
        _schema = schema;

        Title = $"{config.Type} — 속성 편집";
        NameBox.Text = config.Name;
        DescriptionBox.Text = config.Description ?? string.Empty;

        // (EC-16) SourceInitialized 시점(Win32 핸들 생성 직후)에 제목표시줄을 1회 칠하고, 이 창이
        // 열려 있는 동안 사용자가 테마를 바꾸면 다시 칠하도록 구독한다 — OnClosed에서 반드시 해제.
        SourceInitialized += (_, _) => WindowTitleBarTheme.Apply(this);
        _themeChangedHandler = _ => WindowTitleBarTheme.Apply(this);
        ThemeManager.ThemeChanged += _themeChangedHandler;

        BuildFields();
    }

    /// <summary>(EC-16) 창이 닫힐 때 <see cref="ThemeManager.ThemeChanged"/> 구독을 반드시 해제합니다(위 필드 문서 참고).</summary>
    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= _themeChangedHandler;
        base.OnClosed(e);
    }

    /// <summary>
    /// <see cref="_schema"/>의 각 PropertyField마다 라벨+입력 컨트롤+HelpText(+Example)를 한 묶음으로
    /// 그립니다. 스키마가 비어 있으면(아직 이 타입에 대한 PropertySchema가 없는 Phase 7 이전 상태)
    /// 안내 문구만 표시합니다. (FN-03) 모든 행을 다 그린 뒤 <see cref="ApplyConditionalVisibility"/>로
    /// <see cref="PropertyField.VisibleWhenKey"/>가 있는 필드의 표시 여부를 적용합니다 — 조건을 판단할
    /// 대상 컨트롤(예: "mode" ComboBox)이 먼저 만들어져 있어야 하므로 반드시 이 루프가 끝난 뒤에
    /// 호출해야 합니다.
    /// </summary>
    private void BuildFields()
    {
        if (_schema.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "이 노드 타입에 등록된 속성이 아직 없습니다 — Phase 7에서 실제 노드 타입이 " +
                       "등록되면 여기에 속성 입력 폼이 자동으로 채워집니다.",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var field in _schema)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            row.Children.Add(new TextBlock
            {
                Text = field.Required ? $"{field.Label} *" : field.Label,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                FontWeight = FontWeights.SemiBold
            });

            var input = CreateInputControl(field);
            _inputControls[field.Key] = input;
            row.Children.Add(input);

            // 완료 기준 + 개발 지침 4번: HelpText는 값이 비어 있어도 필드 자체는 항상 보여준다.
            row.Children.Add(new TextBlock
            {
                Text = field.HelpText,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            if (!string.IsNullOrWhiteSpace(field.Example))
            {
                row.Children.Add(new TextBlock
                {
                    Text = $"예: {field.Example}",
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            _fieldRows[field.Key] = row;
            FieldsPanel.Children.Add(row);
        }

        ApplyConditionalVisibility();
    }

    /// <summary>
    /// (FN-03) <see cref="_schema"/>에서 <see cref="PropertyField.VisibleWhenKey"/>가 지정된 필드마다,
    /// 그 조건 필드(예: Function 노드의 "mode" ComboBox)의 <b>현재</b> 값이
    /// <see cref="PropertyField.VisibleWhenValue"/>와 같은지에 따라 이 필드의 행(<see cref="_fieldRows"/>)
    /// 전체를 보이거나 감춥니다. 조건 필드가 <see cref="ComboBox"/>이면 <see cref="ComboBox.SelectionChanged"/>를
    /// 구독해 사용자가 값을 바꿀 때마다 즉시 다시 계산합니다(완료 기준 — "ComboBox 전환 시 입력란이
    /// 즉시 전환"). 값을 지우지 않고 <see cref="UIElement.Visibility"/>만 바꾸므로, 감춰진 필드에
    /// 이미 입력해 둔 내용은 다시 보일 때 그대로 남아 있습니다(카드8 "모드별로 따로 저장" 요구 — 값이
    /// 사라지지 않고 필드별로 독립적으로 보존됨).
    /// </summary>
    private void ApplyConditionalVisibility()
    {
        foreach (var field in _schema)
        {
            if (field.VisibleWhenKey is null)
            {
                continue;
            }

            if (!_inputControls.TryGetValue(field.VisibleWhenKey, out var controllingControl) ||
                !_fieldRows.TryGetValue(field.Key, out var dependentRow))
            {
                continue; // 조건 필드나 이 필드 자체가 스키마에 없으면(설정 오류) 항상 표시된 채로 둔다.
            }

            void Refresh() => dependentRow.Visibility =
                ReadValue(controllingControl) == field.VisibleWhenValue ? Visibility.Visible : Visibility.Collapsed;

            Refresh();
            if (controllingControl is ComboBox comboBox)
            {
                comboBox.SelectionChanged += (_, _) => Refresh();
            }
        }
    }

    /// <summary>
    /// <paramref name="field"/>.Type에 맞는 입력 컨트롤을 만들고 <see cref="_config"/>.Properties의
    /// 현재 값(없으면 DefaultValue)으로 채웁니다. (ED-D04) TagRef는 ComboBox와 렌더링 방식(PropCombo
    /// 스타일)은 같지만 선택지 출처가 다르므로 별도 분기로 처리합니다 — ComboBox는 <see cref="PropertyField.Options"/>의
    /// 정적 리터럴 목록을, TagRef는 <see cref="TagCatalog.CurrentTags"/>(구조 설정 트리의 실시간
    /// 태그 목록)를 선택지로 씁니다. Number/CredentialRef/TypedValue는 아직 전용 컨트롤이 없어(각각
    /// 후속 Step 범위) 지금은 TextBox로 대체합니다.
    /// </summary>
    private FrameworkElement CreateInputControl(PropertyField field)
    {
        var currentValue = _config.Properties.TryGetValue(field.Key, out var value)
            ? value?.ToString()
            : field.DefaultValue;

        switch (field.Type)
        {
            case PropertyFieldType.Checkbox:
                return new CheckBox
                {
                    IsChecked = bool.TryParse(currentValue, out var isChecked) && isChecked,
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };

            case PropertyFieldType.ComboBox:
                // (★ 버그 수정, 2026-08-13) 이전엔 Background/Foreground만 직접 지정했는데, WPF
                // ComboBox의 기본 ControlTemplate(토글 버튼·화살표·팝업 부분)은 이 두 속성을
                // TemplateBinding으로 반영하지 않아 다크 테마에서도 OS 기본 흰색 상자로 그대로
                // 보이는 실제 버그였다 — 사용자가 스크린샷으로 "속성편집 창의 테마가 깨지고,
                // 실행모드의 테마도 깨짐"을 보고해 발견(TextBox는 기본 템플릿이 Background/
                // Foreground를 TemplateBinding으로 반영해 문제가 없었음). NodeSharp.UI.Themes의
                // Styles.Controls.xaml에 이미 완전한 ControlTemplate을 가진 "PropCombo" 스타일이
                // ED-B4 포팅 때부터 정의돼 있었지만(테마별 CardBrush/Border2Brush/AccBrush를 직접
                // 참조하는 토글 버튼·팝업·ComboBoxItem까지 전부 포함) 실제로 어디에도 적용된 적이
                // 없던 "만들어졌지만 쓰이지 않은" 리소스였음을 확인 — ThemeManager.Apply가
                // Styles.Controls.xaml을 항상 app.Resources에 병합해두므로 FindResource로 바로
                // 사용 가능하다. Background/Foreground 직접 지정 대신 이 스타일을 적용해 토글
                // 버튼·화살표·드롭다운 팝업·항목(ComboBoxItem)까지 전체가 현재 테마를 따르도록
                // 수정했다.
                var comboBox = new ComboBox
                {
                    Style = (Style)FindResource("PropCombo")
                };
                foreach (var option in field.Options ?? Array.Empty<string>())
                {
                    comboBox.Items.Add(option);
                }
                if (currentValue is not null && comboBox.Items.Contains(currentValue))
                {
                    comboBox.SelectedItem = currentValue;
                }
                return comboBox;

            case PropertyFieldType.TagRef:
                // (ED-D04) 이전엔 TagRef가 바로 위 ComboBox 분기를 그대로 공유해 field.Options가
                // 항상 비어 있었으므로(Options는 정적 리터럴 선택지용이지 태그 목록용이 아님) TagRef는
                // 항상 빈 콤보박스로 표시됐다(이 클래스 상단 문서의 예전 "지금은 빈 콤보박스로 표시"
                // 문구 참고). 지금부터는 TagCatalog.CurrentTags(구조 설정 트리가 RenderTree마다
                // 갱신하는 정적 스냅샷 — 팝업이 아니라 이미 항상 열려있는 "구조 설정" 탭 데이터를
                // 그대로 재사용, 자세한 판단 경위는 StructureView 클래스 remarks ED-D04 항목·03번
                // Step맵 ED-D04 항목 참고)를 실제 선택지로 채운다. 화면에 보이는 항목은
                // TagCatalogEntry(사람이 읽는 "장비/PLC/디바이스맵/태그" 경로, ToString()이 이 값을
                // 반환)지만, Properties에 실제로 저장/비교되는 값은 항상 TagCatalogEntry.Id(불변
                // GUID) — ReadValue가 이를 꺼낸다. 이렇게 하면 구조 설정에서 태그 이름만 바꿔도(Id는
                // 그대로) 이미 선택된 연동이 끊기지 않는다(완료 기준).
                var tagCombo = new ComboBox
                {
                    Style = (Style)FindResource("PropCombo")
                };
                foreach (var tag in TagCatalog.CurrentTags)
                {
                    tagCombo.Items.Add(tag);
                }
                if (currentValue is not null)
                {
                    var matchedTag = TagCatalog.CurrentTags.FirstOrDefault(t => t.Id == currentValue);
                    if (matchedTag is not null)
                    {
                        tagCombo.SelectedItem = matchedTag;
                    }
                }
                return tagCombo;

            case PropertyFieldType.Password:
                var passwordBox = new PasswordBox
                {
                    Background = (Brush)FindResource("ControlBackgroundBrush"),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };
                if (currentValue is not null)
                {
                    passwordBox.Password = currentValue;
                }
                return passwordBox;

            case PropertyFieldType.Code:
                return new TextBox
                {
                    Text = currentValue ?? string.Empty,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 80,
                    FontFamily = new FontFamily("Consolas"),
                    Background = (Brush)FindResource("ControlBackgroundBrush"),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };

            case PropertyFieldType.Color:
                // (EC-20) "선택..." 버튼이 ColorPickerDialog를 모달로 열고, 고른 #RRGGBB 값을
                // TextBox에 채운다 — 취소하면(openPicker가 null 반환) TextBox는 그대로 둔다.
                return CreatePickerControl(currentValue, "선택...", () =>
                {
                    var dialog = new ColorPickerDialog(currentValue) { Owner = this };
                    return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
                });

            case PropertyFieldType.Icon:
                // (EC-20) "선택..." 버튼이 IconPickerDialog를 모달로 열고, 클릭한 아이콘의 글리프를
                // TextBox에 채운다 — 이모지 등을 TextBox에 직접 타이핑하는 EC-19 방식도 그대로 된다.
                return CreatePickerControl(currentValue, "선택...", () =>
                {
                    var dialog = new IconPickerDialog { Owner = this };
                    return dialog.ShowDialog() == true ? dialog.SelectedGlyph : null;
                });

            default:
                // Text/Number/CredentialRef/TypedValue — 전용 컨트롤은 각각 후속 Step 범위라 지금은
                // 단순 TextBox로 대체한다.
                return new TextBox
                {
                    Text = currentValue ?? string.Empty,
                    Background = (Brush)FindResource("ControlBackgroundBrush"),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };
        }
    }

    /// <summary>
    /// (EC-20) <see cref="PropertyFieldType.Color"/>/<see cref="PropertyFieldType.Icon"/> 공용 —
    /// TextBox(직접 입력 가능) + <paramref name="buttonLabel"/> 버튼(누르면 <paramref name="openPicker"/>가
    /// 피커 다이얼로그를 열고 결과 문자열을 돌려줌, 취소 시 <c>null</c>)을 한 줄에 담은 합성
    /// 컨트롤을 만듭니다. 버튼 클릭 결과가 <c>null</c>이 아니면 TextBox에 채우고, <c>null</c>이면
    /// (취소) TextBox를 그대로 둡니다. 반환된 <see cref="Grid"/>의 <see cref="FrameworkElement.Tag"/>에
    /// 내부 TextBox를 담아둬 <see cref="ReadValue"/>가 <c>Grid { Tag: TextBox }</c> 패턴으로 최종
    /// 값을 꺼낼 수 있게 합니다.
    /// </summary>
    private Grid CreatePickerControl(string? currentValue, string buttonLabel, Func<string?> openPicker)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox
        {
            Text = currentValue ?? string.Empty,
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush")
        };
        Grid.SetColumn(textBox, 0);

        var button = new Button
        {
            Content = buttonLabel,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(button, 1);
        button.Click += (_, _) =>
        {
            var picked = openPicker();
            if (picked is not null)
            {
                textBox.Text = picked;
            }
        };

        grid.Children.Add(textBox);
        grid.Children.Add(button);
        grid.Tag = textBox;
        return grid;
    }

    /// <summary>
    /// 입력 컨트롤 하나에서 현재 값을 문자열로 꺼냅니다(컨트롤 종류별로 읽는 방법이 다름). (ED-D04)
    /// TagRef 콤보박스(<see cref="CreateInputControl"/>)는 화면엔 <see cref="TagCatalogEntry"/>(표시
    /// 경로)가 보이지만 실제로 저장할 값은 그 항목의 Id이므로, 그 경우를 먼저 매칭해 <c>.Id</c>를
    /// 꺼낸다 — 일반 ComboBox(문자열 항목)의 기존 동작은 그대로 유지된다. (EC-20) Color/Icon
    /// 필드(<see cref="CreatePickerControl"/>이 만든 <see cref="Grid"/>)는 <see cref="FrameworkElement.Tag"/>에
    /// 담아둔 내부 TextBox에서 값을 꺼낸다 — 일반 TextBox 케이스보다 먼저 매칭해야 하므로 그 위에 둔다.
    /// </summary>
    private static string ReadValue(FrameworkElement control) => control switch
    {
        CheckBox checkBox => (checkBox.IsChecked == true).ToString(),
        ComboBox { SelectedItem: TagCatalogEntry tagEntry } => tagEntry.Id,
        ComboBox comboBox => comboBox.SelectedItem as string ?? comboBox.Text,
        PasswordBox passwordBox => passwordBox.Password,
        Grid { Tag: TextBox innerTextBox } => innerTextBox.Text,
        TextBox textBox => textBox.Text,
        _ => string.Empty
    };

    /// <summary>
    /// "완료" — 모든 입력 컨트롤 값을 모아 새 Properties 딕셔너리를 만들고, 이름·설명까지 반영한 새
    /// NodeConfig를 <see cref="UpdatedConfig"/>에 담은 뒤 <see cref="Window.DialogResult"/>를
    /// true로 창을 닫습니다. (EC-11) 설명란이 빈 문자열이면 <c>null</c>로 정규화해, 문서 배지 표시
    /// 여부를 판단하는 <c>string.IsNullOrWhiteSpace</c> 검사가 일관되게 동작하도록 합니다.
    /// </summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var updatedProperties = new Dictionary<string, object?>(_config.Properties);
        foreach (var (key, control) in _inputControls)
        {
            updatedProperties[key] = ReadValue(control);
        }

        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text;
        UpdatedConfig = _config with { Name = NameBox.Text, Properties = updatedProperties, Description = description };
        DialogResult = true;
        Close();
    }

    /// <summary>"취소" — 아무 것도 반영하지 않고(<see cref="UpdatedConfig"/>는 null 그대로) 창을 닫습니다.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
