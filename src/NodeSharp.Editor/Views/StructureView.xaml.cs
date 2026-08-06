using System.Windows.Controls;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 구조 설정 뷰
/// 역활 및 기능 : MainWindow 본문 우측에 항상 표시되는 구조 설정 자리(ED-B2a 시점에는 빈 화면)
///
/// (ED-B2a) 02번 문서 8번 탭 카드15가 확정한 "항상 분할 도킹" 설계에 따라, 이 뷰는 화면 전환 없이
/// <see cref="FlowCanvasView"/>와 GridSplitter를 사이에 두고 항상 동시에 보입니다. 장비→PLC→
/// 디바이스맵→태그→스케일→알람 6단계 트리 등 실제 내용은 Phase 9(구조 설정)에서 채워집니다.
/// </summary>
public partial class StructureView : UserControl
{
    /// <summary>XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴).</summary>
    public StructureView()
    {
        InitializeComponent();
    }
}
