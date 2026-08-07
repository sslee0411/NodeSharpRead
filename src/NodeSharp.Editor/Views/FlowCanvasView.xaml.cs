using System.Windows.Controls;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Flow 캔버스 뷰
/// 역활 및 기능 : MainWindow 본문 좌측에 항상 표시되는 Flow 캔버스 자리(ED-B2a 시점에는 빈 화면)
///
/// (ED-B2a) 02번 문서 8번 탭 카드15가 확정한 "항상 분할 도킹" 설계에 따라, 이 뷰는 화면 전환 없이
/// <see cref="StructureView"/>와 GridSplitter를 사이에 두고 항상 동시에 보입니다. 실제 노드
/// 배치·와이어 연결 UI는 Phase 6(Editor 캔버스)에서 이 UserControl 안에 채워집니다.
/// (EC-01a) 좌측에 <see cref="PaletteView"/>(노드 팔레트)를 추가했습니다 — 실제 캔버스 렌더링·드래그
/// 배치는 다음 Step(EC-01b)에서 우측 자리표시자를 대체합니다.
/// </summary>
public partial class FlowCanvasView : UserControl
{
    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public FlowCanvasView()
    {
        InitializeComponent();
    }
}
