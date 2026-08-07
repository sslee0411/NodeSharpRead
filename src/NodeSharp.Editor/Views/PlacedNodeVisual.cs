using System.Windows;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 배치된 노드 시각 정보
/// 역활 및 기능 : 캔버스에 실제로 그려진 노드 카드 하나의 위치·포트 개수를 기억하는 표시 전용 모델
///
/// FlowCanvasView가 포트 위치(입력은 왼쪽 가장자리, 출력은 오른쪽 가장자리)를 계산하거나 와이어를
/// 다시 그릴 때 필요한 최소 정보만 담습니다(EC-02). <see cref="NodeSharp.Contracts.Models.NodeConfig"/>
/// (저장용 모델)와는 별개로, 화면 좌표 계산에만 쓰는 표시 전용 데이터입니다.
/// </summary>
public sealed class PlacedNodeVisual
{
    /// <summary>카드의 좌상단 좌표(<paramref name="left"/>/<paramref name="top"/>)와 크기, 포트 개수를 그대로 담습니다.</summary>
    public PlacedNodeVisual(string nodeId, double left, double top, double width, double height, int inputs, int outputs)
    {
        NodeId = nodeId;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Inputs = inputs;
        Outputs = outputs;
    }

    /// <summary>이 시각 정보가 나타내는 노드의 <see cref="NodeSharp.Contracts.Models.NodeConfig.Id"/>.</summary>
    public string NodeId { get; }

    /// <summary>카드 좌상단 X좌표(캔버스 기준).</summary>
    public double Left { get; }

    /// <summary>카드 좌상단 Y좌표(캔버스 기준).</summary>
    public double Top { get; }

    /// <summary>카드 폭(EC-02 시점엔 모든 카드가 고정 폭).</summary>
    public double Width { get; }

    /// <summary>카드 높이(EC-02 시점엔 모든 카드가 고정 높이).</summary>
    public double Height { get; }

    /// <summary>이 노드의 입력 포트 개수.</summary>
    public int Inputs { get; }

    /// <summary>이 노드의 출력 포트 개수.</summary>
    public int Outputs { get; }

    /// <summary>0부터 시작하는 <paramref name="portIndex"/>번째 입력 포트의 캔버스 좌표(왼쪽 가장자리)입니다.</summary>
    public Point GetInputPortPosition(int portIndex) =>
        new(Left, Top + Height * (portIndex + 1) / (Inputs + 1));

    /// <summary>0부터 시작하는 <paramref name="portIndex"/>번째 출력 포트의 캔버스 좌표(오른쪽 가장자리)입니다.</summary>
    public Point GetOutputPortPosition(int portIndex) =>
        new(Left + Width, Top + Height * (portIndex + 1) / (Outputs + 1));
}
