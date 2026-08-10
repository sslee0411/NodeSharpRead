using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Information 패널 뷰
/// 역활 및 기능 : 캔버스에서 선택한 노드 하나의 타입 설명·HelpText/Example·인스턴스 설명을 모아 보여주는 읽기 전용 사이드바
///
/// (EC-11) Node-RED 5.0의 "Information" 사이드바(02번 문서 9번 탭 카드16)에 대응합니다.
/// <see cref="FlowCanvasView.SelectionChanged"/>가 <see cref="Update"/>를 호출하도록 <c>MainWindow</c>가
/// 연결하며, 이 뷰 자신은 <see cref="FlowCanvasView"/>를 직접 참조하지 않습니다(선택 상태를 값으로만
/// 전달받는 단방향 구조 — StructureView/FlowCanvasView가 서로를 직접 참조하지 않는 기존 관례와 동일).
/// 정확히 노드 하나가 선택돼 있으면: (1) 노드 타입 이름·분류(<see cref="INodeTypeDescriptor.Category"/>,
/// 타입이 아직 등록되지 않았으면 "(등록되지 않은 타입)"), (2) <see cref="INodeTypeDescriptor.PropertySchema"/>의
/// 각 <see cref="PropertyField"/>마다 Label+HelpText(+Example) — <see cref="NodePropertyDialog"/>의
/// BuildFields와 같은 정보를 읽기 전용으로 다시 보여주는 셈이지만 그쪽은 "편집"이 목적이고 이쪽은
/// "선택한 노드에 대한 문서 열람"이 목적이라 별도 뷰로 분리했습니다. (3) 노드 인스턴스의
/// <see cref="NodeConfig.Description"/>(채워져 있을 때만, CT-07 PropertyField와는 별개 필드 —
/// NodeConfig 자체 문서의 EC-11 remarks 참고)을 순서대로 표시합니다. 선택이 없거나(0개) 여러 개
/// 선택돼 있으면(2개 이상) 안내 문구만 남기고 내용을 비웁니다.
/// </summary>
public partial class InformationPanelView : UserControl
{
    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public InformationPanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// <paramref name="config"/>가 <c>null</c>이면(선택 없음 또는 다중 선택) 안내 문구만 남기고
    /// 내용을 비웁니다. 아니면 <paramref name="descriptor"/>(아직 등록 안 된 타입이면 <c>null</c>)와
    /// <paramref name="config"/>를 바탕으로 <see cref="ContentPanel"/>을 다시 채웁니다.
    /// </summary>
    public void Update(NodeConfig? config, INodeTypeDescriptor? descriptor)
    {
        ContentPanel.Children.Clear();

        if (config is null)
        {
            EmptySelectionHint.Visibility = Visibility.Visible;
            ContentScroll.Visibility = Visibility.Collapsed;
            return;
        }

        EmptySelectionHint.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;

        AddHeader(config, descriptor);

        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            AddSection("설명", config.Description);
        }

        var schema = descriptor?.PropertySchema ?? Array.Empty<PropertyField>();
        if (schema.Count == 0)
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = "이 노드 타입에 등록된 속성이 아직 없습니다 — Phase 7에서 실제 노드 타입이 " +
                       "등록되면 여기에 필드별 설명이 자동으로 채워집니다.",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        foreach (var field in schema)
        {
            AddFieldSection(field);
        }
    }

    /// <summary>선택된 노드의 이름·타입·분류를 맨 위에 고정 표시합니다.</summary>
    private void AddHeader(NodeConfig config, INodeTypeDescriptor? descriptor)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = config.Name,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });

        var category = descriptor?.Category ?? "(등록되지 않은 타입)";
        ContentPanel.Children.Add(new TextBlock
        {
            Text = $"{config.Type} · {category}",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 10)
        });
    }

    /// <summary>제목 하나 + 본문 텍스트 하나로 된 구획을 추가합니다(설명 섹션 전용).</summary>
    private void AddSection(string title, string body)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        ContentPanel.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
    }

    /// <summary>
    /// <see cref="PropertyField"/> 하나마다 라벨+HelpText(+Example)를 <see cref="NodePropertyDialog"/>의
    /// 필드 구성과 같은 정보로(단, 입력 컨트롤 없이 읽기 전용 텍스트로만) 추가합니다.
    /// </summary>
    private void AddFieldSection(PropertyField field)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        row.Children.Add(new TextBlock
        {
            Text = field.Required ? $"{field.Label} *" : field.Label,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontWeight = FontWeights.SemiBold
        });

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

        ContentPanel.Children.Add(row);
    }
}
