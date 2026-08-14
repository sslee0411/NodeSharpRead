using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
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
/// 누르면 아무 것도 만들지 않고 닫습니다. TagRef는 이 Step 요구대로 ComboBox로 렌더링하지만, 실제
/// 태그 목록(구조 설정 데이터)은 Phase 9 이후에나 채워지므로 지금은 빈 콤보박스로 표시됩니다.
/// (EC-11) 이름 바로 아래에 "설명"(<see cref="NodeConfig.Description"/>) 입력란도 고정 필드로
/// 함께 제공합니다 — 값이 있으면 캔버스 카드에 문서 배지가 표시되고 클릭 시 팝업으로 이 텍스트를
/// 그대로 보여줍니다.
/// (EC-16, ★ 사용자 요청) 이 창은 EC-03 설계 당시 판단대로 OS 기본 제목표시줄(WindowStyle 기본값)을
/// 그대로 쓰지만, 사용자가 스크린샷으로 "팝업 창의 타이틀바는 아직 OS 기본(흰색) 그대로"임을 보고해
/// <see cref="WindowTitleBarTheme.Apply"/>(DWM API)로 그 네이티브 제목표시줄의 색상만 현재 테마에
/// 맞춥니다 — 창을 커스텀 타이틀바로 바꾸는 EC-03 판단 자체를 뒤집지는 않습니다(자세한 배경은
/// <see cref="WindowTitleBarTheme"/> 클래스 문서 참고).
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
    /// 현재 값(없으면 DefaultValue)으로 채웁니다. TagRef/ComboBox는 둘 다 ComboBox로 렌더링합니다
    /// (이 Step의 완료 기준). Number/CredentialRef/TypedValue는 아직 전용 컨트롤이 없어(각각 후속
    /// Step 범위) 지금은 TextBox로 대체합니다.
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
            case PropertyFieldType.TagRef:
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

    /// <summary>입력 컨트롤 하나에서 현재 값을 문자열로 꺼냅니다(컨트롤 종류별로 읽는 방법이 다름).</summary>
    private static string ReadValue(FrameworkElement control) => control switch
    {
        CheckBox checkBox => (checkBox.IsChecked == true).ToString(),
        ComboBox comboBox => comboBox.SelectedItem as string ?? comboBox.Text,
        PasswordBox passwordBox => passwordBox.Password,
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
