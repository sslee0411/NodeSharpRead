using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Structure;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 구조 설정 노드 속성 편집 다이얼로그
/// 역활 및 기능 : StructureTreeNode(ED-D01) 1개의 이름/설명/PropertySchema 필드를 편집하는 모달 창
///
/// (ED-D02a/b) 02번 설계문서 8번 탭 카드4가 요구하는 "선택 노드의 PropertySchema를 자동생성 폼으로
/// 렌더링"을 <see cref="NodePropertyDialog"/>(캔버스 노드용, EC-03)와 동일한 레이아웃·입력 컨트롤
/// 선택 로직으로 제공합니다. 다른 점은 데이터 소스입니다 — <see cref="NodePropertyDialog"/>는
/// <c>NodeConfig.Properties</c>(문자열 키 → 값 딕셔너리, record라 불변)를 다루지만, 6종
/// <see cref="StructureTreeNode"/> 구체 클래스는 각자 타입이 정해진 일반 프로퍼티(<c>PlcNode.Host</c>,
/// <c>ScaleNode.RawMin</c> 등)를 직접 갖는 평범한 mutable 클래스입니다. 이 6종 각각에 대해 별도
/// 다이얼로그를 만드는 대신, <see cref="PropertyField.Key"/>가 그 타입의 C# 프로퍼티 이름과 항상
/// 일치하도록 설계돼 있음(<c>StructureTreeNode.cs</c> 참고 — 예: "host"→<c>PlcNode.Host</c>,
/// "rawMin"→<c>ScaleNode.RawMin</c>)을 이용해 리플렉션(<see cref="GetValue"/>/<see cref="SetValue"/>)으로
/// 값을 읽고 쓰는 하나의 범용 다이얼로그로 6종 전부를 처리합니다 — Editor 전용 편의 코드라 성능
/// 민감도가 낮고, 매 릴리스마다 6개의 유사 다이얼로그를 따로 유지보수하는 비용을 피할 수 있어
/// 리플렉션을 선택했습니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>널이 가능한 숫자 필드</b>(<see cref="AlarmNode.HH"/> 등 <c>double?</c>): 입력란을 비워두면
/// <c>null</c>로 저장됩니다(02번 문서 8번 탭 카드3 "비워두면 이 등급은 검사하지 않습니다" 그대로).</item>
/// <item><b>PlcNode.CommType(ComboBox, Options: null)</b>: 설계문서는 "실제 Editor는
/// ProtocolDriverRegistry.RegisteredDrivers.Keys로 동적으로 채운다"고 명시하지만, Editor에는 아직
/// ProtocolDriverRegistry 연동이 없습니다(PD-01a 이후 과제) — 지금은 <see cref="PropertyField.Options"/>가
/// null이면 일반 TextBox로 대체해 값 자체는 자유 입력으로 편집 가능하게 합니다.</item>
/// <item>완료 시 새 인스턴스를 만들지 않고 전달받은 <see cref="StructureTreeNode"/>를 <b>그 자리에서
/// 직접 수정</b>합니다(<c>NodeConfig</c>와 달리 record가 아닌 mutable 클래스이므로 — <c>StructureView</c>가
/// 갖고 있는 트리의 참조가 그대로 최신값을 반영하며, 별도 교체 로직이 필요 없습니다).</item>
/// </list>
/// </remarks>
public partial class StructureNodePropertyDialog : Window
{
    private readonly StructureTreeNode _node;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();

    /// <summary>"완료"로 닫혔으면 true — 취소 시 false(<see cref="_node"/>는 이미 그 자리에서 수정됐으므로, 호출부는 이 값을 트리 재렌더링 여부 판단에만 씁니다).</summary>
    public bool Saved { get; private set; }

    /// <summary><paramref name="node"/>의 현재 값으로 폼을 채웁니다.</summary>
    public StructureNodePropertyDialog(StructureTreeNode node)
    {
        InitializeComponent();

        _node = node;
        Title = $"{TypeLabel(node)} — 속성 편집";
        NameBox.Text = node.Name;
        DescriptionBox.Text = node.Description;

        BuildFields();
    }

    /// <summary>노드 타입의 한글 표시명 — <see cref="Title"/>에만 쓰이는 표시용 문구입니다.</summary>
    private static string TypeLabel(StructureTreeNode node) => node switch
    {
        DeviceNode => "장비",
        PlcNode => "PLC",
        DeviceMapNode => "디바이스맵",
        TagNode => "태그",
        ScaleNode => "스케일",
        AlarmNode => "알람",
        _ => node.GetType().Name,
    };

    /// <summary><see cref="StructureTreeNode.PropertySchema"/>의 각 필드마다 라벨+입력 컨트롤+HelpText(+Example)를 그립니다. NodePropertyDialog.BuildFields와 동일한 구성입니다.</summary>
    private void BuildFields()
    {
        var schema = _node.PropertySchema;
        if (schema.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "이 노드 타입에 등록된 속성이 없습니다.",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var field in schema)
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

    /// <summary><paramref name="field"/>.Type에 맞는 입력 컨트롤을 만들고, <see cref="GetValue"/>로 읽은 <see cref="_node"/>의 현재 값(없으면 DefaultValue)으로 채웁니다. NodePropertyDialog.CreateInputControl과 동일한 컨트롤 선택 로직입니다.</summary>
    private FrameworkElement CreateInputControl(PropertyField field)
    {
        var currentValue = GetValue(field) ?? field.DefaultValue;

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
                if (field.Options is null || field.Options.Count == 0)
                {
                    // (위 클래스 remarks 참고) 정적 옵션이 없으면(예: PlcNode.CommType — 아직 드라이버
                    // 레지스트리 연동이 없음) 일반 TextBox로 자유 입력을 허용한다.
                    return new TextBox
                    {
                        Text = currentValue ?? string.Empty,
                        Background = (Brush)FindResource("ControlBackgroundBrush"),
                        Foreground = (Brush)FindResource("PrimaryTextBrush")
                    };
                }

                var comboBox = new ComboBox { Style = (Style)FindResource("PropCombo") };
                foreach (var option in field.Options)
                {
                    comboBox.Items.Add(option);
                }
                if (currentValue is not null && comboBox.Items.Contains(currentValue))
                {
                    comboBox.SelectedItem = currentValue;
                }
                return comboBox;

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
                return new TextBox
                {
                    Text = currentValue ?? string.Empty,
                    Background = (Brush)FindResource("ControlBackgroundBrush"),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };
        }
    }

    /// <summary><see cref="_node"/>에서 <paramref name="field"/>.Key와 이름이 같은(대소문자 무시) 공개 인스턴스 프로퍼티를 찾아 문자열로 읽습니다. 없으면 null.</summary>
    private string? GetValue(PropertyField field)
    {
        var property = _node.GetType().GetProperty(field.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(_node)?.ToString();
    }

    /// <summary>입력 컨트롤 하나에서 현재 값을 문자열로 꺼냅니다(NodePropertyDialog.ReadValue와 동일).</summary>
    private static string ReadControlValue(FrameworkElement control) => control switch
    {
        CheckBox checkBox => (checkBox.IsChecked == true).ToString(),
        ComboBox comboBox => comboBox.SelectedItem as string ?? comboBox.Text,
        TextBox textBox => textBox.Text,
        _ => string.Empty
    };

    /// <summary>
    /// "완료" — <see cref="_inputControls"/>의 각 값을 리플렉션으로 <see cref="_node"/>의 실제 프로퍼티에
    /// 씁니다. 대상 프로퍼티 타입에 맞춰 <c>int</c>/<c>double</c>/<c>double?</c>/<c>string</c>으로 변환하고,
    /// <c>double?</c> 프로퍼티(<see cref="AlarmNode"/>의 HH/H/L/LL/EQ/NE)는 빈 문자열이면 <c>null</c>로
    /// 씁니다(위 클래스 remarks 참고). 변환에 실패하면(예: 숫자 필드에 문자를 입력) 그 필드만 건너뛰고
    /// 나머지는 정상 반영합니다 — 다이얼로그 전체가 막히는 것보다 낫다고 판단했습니다.
    /// </summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _node.Name = NameBox.Text;
        _node.Description = DescriptionBox.Text;

        foreach (var (key, control) in _inputControls)
        {
            var property = _node.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            var raw = ReadControlValue(control);
            TrySetTypedValue(property, raw);
        }

        Saved = true;
        DialogResult = true;
        Close();
    }

    /// <summary><paramref name="property"/>의 실제 타입(<c>int</c>/<c>double</c>/<c>double?</c>/<c>string</c>)에 맞게 <paramref name="raw"/>를 변환해 씁니다. 변환 실패 시 조용히 건너뜁니다.</summary>
    private void TrySetTypedValue(PropertyInfo property, string raw)
    {
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
        var isNullable = underlyingType is not null;
        var targetType = underlyingType ?? property.PropertyType;

        if (isNullable && string.IsNullOrWhiteSpace(raw))
        {
            property.SetValue(_node, null);
            return;
        }

        if (targetType == typeof(string))
        {
            property.SetValue(_node, raw);
        }
        else if (targetType == typeof(int))
        {
            if (int.TryParse(raw, out var i))
            {
                property.SetValue(_node, i);
            }
        }
        else if (targetType == typeof(double))
        {
            if (double.TryParse(raw, out var d))
            {
                property.SetValue(_node, d);
            }
        }
        else if (targetType == typeof(bool))
        {
            if (bool.TryParse(raw, out var b))
            {
                property.SetValue(_node, b);
            }
        }
    }

    /// <summary>"취소" — 아무 것도 반영하지 않고(<see cref="Saved"/>는 false 그대로) 창을 닫습니다.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
