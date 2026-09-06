using System.Windows;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 시퀀스 에디터 창
/// 역활 및 기능 : 캔버스(FlowCanvasView)와 별개의 독립 WPF Window로 뜨는 Sequence Editor의 뼈대(placeholder) + 역방향 내비게이션 진입점
///
/// (SQ-02) 02번 설계 문서 11번 탭 카드6이 요구하는 "캔버스와 별개의 독립 Window"입니다. 단계 목록
/// 편집 UI 자체는 아직 없고(클래스 XML 문서 참고 — 후속 Step 범위), 이 Step의 책임은 창 자체가
/// <see cref="SequenceWindowManager"/>를 통해 중복 없이 뜨고, 다른 모니터로 옮겨도 위치가 유지되는
/// 것입니다. 이 클래스 자체는 인스턴스 생성/닫힘만 다루고, 실제 중복 방지·위치 기억 로직은
/// <see cref="Core.SequenceWindowManager"/>(별도 클래스, 창 인스턴스를 감싸는 책임 분리 — MainWindow가
/// 직접 <c>new SequenceEditorWindow()</c>를 호출하지 않고 매니저를 거치게 함)에 있습니다.
/// (SQ-03, ★ 보강) "호출하는 Flow 노드 보기" 버튼을 누르면 <see cref="FindCallersRequested"/> 이벤트를
/// 발행합니다 — 이 창 자체는 <c>IFlowNodeIndex</c>/캔버스를 몰라도 되고(창이 아는 것은 "지금 텍스트
/// 상자에 어떤 문자열이 있는가"뿐), 실제 조회·하이라이트는 이 이벤트를 구독하는
/// <see cref="Core.SequenceWindowManager"/>가 담당합니다(책임 분리는 SQ-02와 동일한 원칙).
/// </summary>
public partial class SequenceEditorWindow : Window
{
    /// <summary>"호출하는 Flow 노드 보기" 버튼을 눌렀을 때 발행되며, 인자는 <see cref="SequenceIdBox"/>에 입력된(공백 제거된) 시퀀스 Id입니다.</summary>
    public event Action<string>? FindCallersRequested;

    /// <summary>XAML에서 정의한 컨트롤을 초기화합니다(WPF 표준 패턴).</summary>
    public SequenceEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// (SQ-03) <c>SequenceTriggerNode</c>를 더블클릭해 이 창을 열 때(<c>FlowCanvasView.OnCardMouseLeftButtonDown</c>)
    /// 그 노드의 <c>SequenceId</c>를 미리 채워둡니다 — "편도"로 들어온 시퀀스를 굳이 다시 타이핑하지
    /// 않고 바로 "호출하는 Flow 노드 보기"(역방향)를 눌러볼 수 있게 합니다.
    /// </summary>
    public void SetSequenceId(string sequenceId) => SequenceIdBox.Text = sequenceId;

    private void OnFindCallersClick(object sender, RoutedEventArgs e) =>
        FindCallersRequested?.Invoke(SequenceIdBox.Text.Trim());
}
