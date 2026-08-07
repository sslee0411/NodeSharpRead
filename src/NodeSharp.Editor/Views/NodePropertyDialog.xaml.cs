using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;

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
/// </summary>
public partial class NodePropertyDialog : Window
{
    private readonly NodeConfig _config;
    private readonly IReadOnlyList<PropertyField> _schema;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();

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

        BuildFields();
    }

    /// <summary>
    /// <see cref="_schema"/>의 각 PropertyField마다 라벨+입력 컨트롤+HelpText(+Example)를 한 묶음으로
    /// 그립니다. 스키마가 비어 있으면(아직 이 타입에 대한 PropertySchema가 없는 Phase 7 이전 상태)
    /// 안내 문구만 표시합니다.
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

            FieldsPanel.Children.Add(row);
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
                var comboBox = new ComboBox
                {
                    Background = (Brush)FindResource("ControlBackgroundBrush"),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
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
    /// "완료" — 모든 입력 컨트롤 값을 모아 새 Properties 딕셔너리를 만들고, 이름까지 반영한 새
    /// NodeConfig를 <see cref="UpdatedConfig"/>에 담은 뒤 <see cref="Window.DialogResult"/>를
    /// true로 창을 닫습니다.
    /// </summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var updatedProperties = new Dictionary<string, object?>(_config.Properties);
        foreach (var (key, control) in _inputControls)
        {
            updatedProperties[key] = ReadValue(control);
        }

        UpdatedConfig = _config with { Name = NameBox.Text, Properties = updatedProperties };
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
