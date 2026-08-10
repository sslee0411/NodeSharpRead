using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Core.Commands;
using NodeSharp.Editor.Core.Config;
using NodeSharp.Registry;
// (EC-11 버그 수정, v2.64) NodeSharp.Contracts.Interfaces에도 별도 용도의 IEditorCommand가 이미
// 선언돼 있어(ED-D13이 미리 열어둔 "구조 트리 커맨드 공유" 설계, 이 파일의 AddNodeCommand 등이 구현
// 하는 NodeSharp.Editor.Core.Commands.IEditorCommand와는 다른 타입), 이 네임스페이스를 통째로
// using하면 파일 안의 모든 "IEditorCommand" 참조가 어느 쪽인지 모호해져 CS0104가 발생한다(그 여파로
// AddNodeCommand/AddWireCommand/EditNodePropertiesCommand를 IEditorCommand로 넘기는 곳마다 CS1503도
// 함께 발생). 이 파일이 실제로 필요한 건 INodeTypeDescriptor 하나뿐이라, 네임스페이스 전체 대신
// 이 타입 하나만 별칭으로 가져와 충돌을 원천 차단한다.
using INodeTypeDescriptor = NodeSharp.Contracts.Interfaces.INodeTypeDescriptor;

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
/// (EC-01b) 우측을 실제 <c>Canvas</c>(<c>NodeCanvas</c>)로 바꾸고, 팔레트에서 시작된 WPF 표준
/// 드래그 앤 드롭을 받아(<see cref="OnCanvasDrop"/>) <see cref="NodeConfig"/>를 생성·화면에 렌더링합니다.
/// (EC-02) 각 카드에 입력/출력 포트(<see cref="Ellipse"/>)를 추가하고, 출력 포트를 누른 채
/// 입력 포트 위에서 놓으면 <see cref="Wire"/>가 생성되는 드래그 상호작용을 구현했습니다 — WPF
/// 표준 드래그 앤 드롭(팔레트 배치용)과는 별개로 <see cref="Mouse.Capture(System.Windows.IInputElement)"/>
/// 기반의 자체 메커니즘을 씁니다. 지금은 <c>NodeTypeRegistry</c>에 등록된 실제 노드 타입이 없어
/// (Phase 7 이전) 모든 카드를 입력 1개·출력 1개로 고정합니다 — 노드 타입별 실제 포트 개수
/// (<c>INodeTypeDescriptor.DefaultInputs</c>/<c>DefaultOutputs</c>) 반영은 Phase 7 이후로 미룹니다.
/// (EC-03) 카드를 더블클릭하면 <see cref="NodePropertyDialog"/>가 뜹니다(<see cref="OpenPropertyDialog"/>) —
/// 이 뷰가 직접 만든 <c>NodeTypeRegistry</c>(팔레트와 별개 인스턴스, EC-01a와 동일한 패턴)에서
/// 해당 타입의 PropertySchema를 찾아 넘기고, "완료"로 닫히면 <see cref="_nodeConfigs"/>와 카드에
/// 표시된 이름을 갱신합니다.
/// (EC-04) <see cref="_flowStore"/>(<see cref="FlowStore"/>)로 flows.json 저장/로드를 붙였습니다.
/// 이 뷰가 로드될 때(<see cref="OnLoaded"/>) 저장된 내용이 있으면 노드·와이어를 그대로 복원하고,
/// <see cref="SaveFlowAsync"/>를 호출하면(<c>MainWindow</c>의 "저장" 메뉴/Ctrl+S) 지금 캔버스 상태를
/// <c>flows.json</c>에 원자적으로 저장합니다. 노드 카드의 캔버스 좌표는 <see cref="NodeConfig.X"/>/
/// <see cref="NodeConfig.Y"/>에 직접 저장하도록 바뀌어(EC-04 신규 필드), <see cref="RenderNode"/>가
/// 별도의 드롭 좌표 매개변수 없이 <c>config.X</c>/<c>config.Y</c>를 그대로 읽습니다.
/// (EC-05, ★ 사용자 요청) 상단 <c>FlowTabStrip</c>에 여러 Flow 탭(<see cref="FlowTabInfo"/>)을
/// 추가/전환/삭제할 수 있습니다. 이전에는 노드 전체가 고정 <c>FlowId</c>("f1") 하나에 속했지만,
/// 이제 <see cref="_nodeConfigs"/>/<see cref="_wires"/>는 <b>모든 탭의 데이터를 함께</b> 보관하고
/// 각 <see cref="NodeConfig.FlowId"/>로 소속 탭을 구분합니다 — 캔버스에는 <see cref="_activeFlowId"/>
/// 탭의 노드·와이어만 그려집니다(<see cref="SwitchToFlow"/>가 <c>NodeCanvas</c>를 비우고 다시
/// 그리는 방식, WPF 요소 show/hide보다 단순함). flows.json은 이제 <see cref="FlowDefinition"/>
/// 목록(탭 개수만큼)이고, <see cref="SaveFlowAsync"/>는 탭별로 자기 소속 노드·와이어만 모아 각각의
/// <see cref="FlowDefinition"/>을 만듭니다. 설계 판단 근거(단일 스키마 → 리스트 스키마 전환,
/// Runner 쪽 <c>StartupSequencer</c>/<c>FlowDeployer</c> 동시 수정)는
/// <c>NodeSharp.Editor.csproj</c>/<c>NodeSharp.Runner.csproj</c>의 EC-05 블록을 참고하십시오.
/// (EC-06) 카드를 한 번 클릭(<c>e.ClickCount == 1</c>)하면 선택 상태가 되어 테두리가
/// <c>AccentBrush</c>로 강조됩니다(<see cref="SelectNode"/>) — 더블클릭(속성 다이얼로그)과는
/// <see cref="OnCardMouseLeftButtonDown"/> 안에서 클릭 횟수로 분기합니다. 선택된 노드가 있는
/// 상태에서 Ctrl+C를 누르면(<c>MainWindow</c>) <see cref="CopySelectedNode"/>가 그 노드의
/// <see cref="NodeConfig"/>를 내부 클립보드(<see cref="_clipboardNode"/>)에 복제해 담고, Ctrl+V를
/// 누르면 <see cref="PasteNode"/>가 새 Id를 재발급해(<see cref="_nextNodeSeq"/>, 원본과 Id가
/// 겹치지 않도록) 지금 활성 탭(<see cref="_activeFlowId"/>)에 살짝 어긋난 위치로 붙여넣습니다.
/// (EC-07) 노드 추가(팔레트 드롭·붙여넣기)/와이어 연결/속성 편집 3가지 캔버스 편집을
/// <see cref="NodeSharp.Editor.Core.Commands.IEditorCommand"/> 구현체(이 클래스의 중첩 클래스
/// <see cref="AddNodeCommand"/>/<see cref="AddWireCommand"/>/<see cref="EditNodePropertiesCommand"/>)로
/// 감싸 <see cref="_history"/>(<see cref="NodeSharp.Editor.Core.Commands.CommandHistory"/>, 최대
/// 50단계)에 실행합니다 — 각 커맨드는 데이터(<see cref="_nodeConfigs"/>/<see cref="_wires"/>)만
/// 바꾸고 <see cref="RedrawActiveTab"/>(<see cref="SwitchToFlow"/>에서 화면 그리기 부분만 분리한
/// 메서드) 하나로 화면을 데이터와 다시 맞춥니다. <see cref="Undo"/>/<see cref="Redo"/>(공개, Ctrl+Z/
/// Ctrl+Y — <c>MainWindow</c>가 호출)가 <see cref="_history"/>를 그대로 위임합니다. 탭 관리(추가/
/// 전환/삭제, EC-05)는 이 Undo 대상에 포함하지 않습니다(설계 근거: 03번 Step맵 EC-07 desc "캔버스
/// 커맨드부터 시작" — 구조 트리 커맨드 공유는 ED-D13 범위).
/// (EC-08) <see cref="RenderNode"/>가 카드를 그릴 때마다 <c>config.Type</c>이 <see cref="_registry"/>
/// (Descriptors)에 있는지 확인합니다 — 없으면 <c>RT-02a</c>의 <c>MissingNode</c>와 같은 개념
/// ("존재하지 않는 노드 타입")을 Editor 쪽에서 독립적으로 판정해 제목을 "⚠ {Type}", 부제목을
/// "missing type"으로 바꾸고 <see cref="ApplyCardBorder"/>가 <c>RedBrush</c>로 테두리를 강조합니다.
/// Editor와 Runner는 별도 프로세스(ED-B0.6 결정)이므로 실제 배포 결과를 기다리지 않고, flows.json을
/// 불러오거나 캔버스를 다시 그릴 때마다 이 뷰 자신의 <c>NodeTypeRegistry</c> 기준으로 매번 다시
/// 판정합니다.
/// (EC-10) <see cref="_selectedNodeIds"/>가 단일 선택에서 다중 선택(HashSet)으로 확장됐습니다 —
/// 일반 클릭은 여전히 <see cref="SelectNode"/>(하나만 선택), Ctrl+클릭은
/// <see cref="ToggleNodeSelection"/>(추가/제거)입니다. 2개 이상 선택한 채 Ctrl+G를 누르면
/// (<c>MainWindow</c>) <see cref="GroupSelectedNodes"/>가 새 <see cref="GroupDefinition"/>을
/// 만들어 <see cref="_groups"/>에 저장하고, Ctrl+Shift+G(<see cref="UngroupSelectedGroup"/>)가
/// 선택된 노드가 속한 그룹을 해제합니다. <see cref="RedrawActiveTab"/>이 그룹마다
/// <see cref="RenderGroup"/>을 호출해 펼친 그룹은 멤버 카드를 감싸는 테두리 박스+이름표
/// (<see cref="RenderExpandedGroupBox"/>)로, 접힌 그룹은 멤버 카드 대신 박스 하나
/// (<see cref="RenderCollapsedGroup"/>)로 그립니다 — 접힌 그룹의 멤버는 카드 자체를 그리지 않고
/// 그 노드에 닿는 와이어도 함께 숨깁니다. <see cref="SaveFlowAsync"/>/<see cref="LoadFlowAsync"/>가
/// <see cref="FlowDefinition.Groups"/>로 그룹을 함께 저장/복원합니다. 그룹 생성/접기는
/// <see cref="_history"/>(EC-07 CommandHistory) 대상이 아닙니다(탭 관리와 같은 이유로 범위 밖 —
/// 설계 근거: 03번 Step맵 EC-10 desc).
/// (EC-11) <see cref="RenderNode"/>가 <c>config.Description</c>이 채워진 카드 우측 상단에 "📝" 문서
/// 배지를 추가로 그립니다 — 클릭하면(<c>e.Handled = true</c>로 카드 자체의 선택/더블클릭 처리로
/// 버블링되지 않게 막고) <see cref="MessageBox"/> 팝업으로 전체 설명 텍스트를 보여줍니다. 선택 상태가
/// 바뀔 때마다(<see cref="SelectNode"/>/<see cref="ToggleNodeSelection"/>/<see cref="RedrawActiveTab"/>)
/// <see cref="SelectionChanged"/> 이벤트를 발생시켜, 정확히 노드 하나가 선택돼 있으면 그
/// <see cref="NodeConfig"/>와 <see cref="_registry"/> 기준 <see cref="INodeTypeDescriptor"/>를, 아니면
/// <c>(null, null)</c>을 전달합니다 — <c>MainWindow</c>가 이 이벤트를 <see cref="Views.InformationPanelView"/>(우측
/// "Information" 탭, 02번 문서 9번 탭 카드16의 Node-RED 5.0 명칭 채택)에 연결해 선택한 노드 타입의
/// HelpText/Example과 인스턴스 Description을 읽기 전용으로 보여줍니다.
/// (EC-12) <see cref="SearchNodes"/>(공개)가 모든 Flow 탭에 걸쳐 노드 이름/속성 값을 대소문자 구분
/// 없이 검색해 <see cref="NodeSearchResult"/> 목록을 돌려줍니다 — <c>MainWindow</c>가 Ctrl+F로
/// <see cref="Views.ExplorerPanelView"/>(EC-11 Information과 짝을 이루는 "Explorer 패널", 같은
/// TabControl의 세 번째 탭)의 검색어 변경 이벤트를 이 메서드에 연결합니다. 결과를 클릭하면
/// <see cref="NavigateToNode"/>(공개)가 해당 Flow 탭으로 전환하고(접힌 그룹 소속이면 먼저 펼치고)
/// <see cref="SelectNode"/>로 선택 상태를 줘 하이라이트합니다.
/// </summary>
public partial class FlowCanvasView : UserControl
{
    // EC-02 시점엔 모든 카드를 이 고정 크기·고정 포트 개수(입력1/출력1)로 그린다(위 클래스 주석 참고).
    private const double NodeCardWidth = 120;
    private const double NodeCardHeight = 40;
    private const double PortRadius = 5;

    // EC-03 PropertySchema 조회 전용 — 팔레트(PaletteView)와는 별개 인스턴스(EC-01a와 동일 패턴).
    private readonly NodeTypeRegistry _registry = new(contractsVersion: "1.0.0");

    // EC-04 flows.json 저장/로드 전용 창구(순수 System.IO 래퍼, 클래스 자체 주석 참고).
    private readonly FlowStore _flowStore = new();
    private bool _flowLoaded;

    // (EC-05) 모든 Flow 탭의 데이터를 함께 담는 전역 딕셔너리 — NodeConfig.FlowId로 소속 탭을 구분한다.
    // 노드 Id는 탭이 달라도 항상 전역적으로 유일해야 한다(Wire.SourceNodeId/TargetNodeId가 탭 구분 없이
    // Id만으로 노드를 참조하고, Runner 배포 시(FlowDeployer)에도 여러 탭의 Nodes가 하나로 병합되므로).
    private readonly Dictionary<string, NodeConfig> _nodeConfigs = new();
    private readonly Dictionary<string, TextBlock> _nodeLabels = new();
    private readonly List<Wire> _wires = new();
    private readonly Dictionary<string, PlacedNodeVisual> _nodeVisuals = new();
    private int _nextNodeSeq = 1;

    // (EC-05) Flow 탭 목록·현재 활성 탭·다음 탭 Id 발급 순번. 최초 상태는 탭 1개("f1", "Flow 1") —
    // LoadFlowAsync가 저장된 flows.json을 찾으면 이 기본 탭을 지우고 불러온 탭들로 교체한다.
    private readonly List<FlowTabInfo> _flowTabs = new() { new FlowTabInfo("f1", "Flow 1") };
    private string _activeFlowId = "f1";
    private int _nextFlowTabSeq = 2;

    // EC-02 와이어 드래그 진행 상태 — 출력 포트를 누르는 순간부터 마우스를 놓을 때까지만 값이 있다.
    private PortHandle? _dragSourcePort;
    private Line? _dragPreviewLine;
    private PortHandle? _hoveredInputPort;

    // (EC-06, EC-10 확장) 카드 선택·복사/붙여넣기 상태. _nodeCards는 선택 시 테두리 강조를 위한
    // Border 참조 보관용(RenderNode가 채움, RedrawActiveTab이 다시 그릴 때마다 함께 비움).
    // _selectedNodeIds는 탭 전환·재렌더링마다 초기화되지만(그 탭에 없는 노드를 계속 가리키면 안
    // 되므로) _clipboardNode는 탭을 넘나들며 계속 유지된다 — 다른 탭에 붙여넣는 것도 자연스러운
    // 동작이라 판단(활성 탭 기준으로 FlowId를 다시 매기므로 항상 붙여넣는 시점의 탭에 정확히
    // 들어간다). (EC-10) 단일 선택이던 _selectedNodeId를 HashSet 기반 다중 선택으로 확장 — 일반
    // 클릭은 여전히 "이것 하나만 선택"(SelectNode)이고, Ctrl+클릭만 추가/제거를 토글한다
    // (ToggleNodeSelection) — 그룹으로 묶을 노드 여러 개를 고르기 위한 확장이며, 카드 하나만
    // 고르는 기존 EC-06 동작(선택→Ctrl+C 복사 등)은 그대로 유지된다.
    private readonly Dictionary<string, Border> _nodeCards = new();
    private readonly HashSet<string> _selectedNodeIds = new();
    private NodeConfig? _clipboardNode;
    private const double PasteOffset = 24;

    /// <summary>
    /// (EC-11) 선택 상태가 바뀔 때마다(단일 선택/해제/다중 선택/탭 전환·Undo·Redo로 인한 재렌더링)
    /// 발생합니다 — 정확히 노드 하나가 선택돼 있으면 그 <see cref="NodeConfig"/>와 <see cref="_registry"/>에
    /// 등록된 해당 타입의 <see cref="INodeTypeDescriptor"/>(아직 등록 안 된 타입이면 <c>null</c> —
    /// Phase 7 이전엔 항상 이 경우)를 함께 전달하고, 0개 또는 2개 이상 선택돼 있으면 <c>(null, null)</c>을
    /// 전달합니다. <c>MainWindow</c>가 이 이벤트를 Information 패널
    /// (<see cref="Views.InformationPanelView"/>)에 연결해 "다른 노드 선택 시 즉시 갱신"을 구현합니다.
    /// </summary>
    public event Action<NodeConfig?, INodeTypeDescriptor?>? SelectionChanged;

    /// <summary>
    /// <see cref="_selectedNodeIds"/>의 현재 상태를 읽어 <see cref="SelectionChanged"/>를 발생시킵니다.
    /// <see cref="SelectNode"/>/<see cref="ToggleNodeSelection"/>/<see cref="RedrawActiveTab"/> 끝에서
    /// 호출합니다.
    /// </summary>
    private void RaiseSelectionChanged()
    {
        if (SelectionChanged is null)
        {
            return;
        }

        if (_selectedNodeIds.Count == 1 && _nodeConfigs.TryGetValue(_selectedNodeIds.First(), out var config))
        {
            _registry.Descriptors.TryGetValue(config.Type, out var descriptor);
            SelectionChanged.Invoke(config, descriptor);
        }
        else
        {
            SelectionChanged.Invoke(null, null);
        }
    }

    // (EC-07) 노드 추가/와이어 연결/속성 편집을 Undo/Redo 가능하게 만드는 커맨드 히스토리(최대 50단계,
    // 클래스 자체 주석 참고). 지금은 이 뷰만 쓰지만 IEditorCommand 인터페이스 자체는 ED-D13에서
    // 구조 트리 커맨드도 같은 스택을 공유하도록 미리 열어둔 설계(02번 문서 8번 탭 카드16).
    private readonly CommandHistory _history = new();

    // (EC-10) 모든 Flow 탭의 그룹을 함께 담는 전역 딕셔너리 — _nodeConfigs/_wires와 동일한 패턴으로,
    // GroupDefinition 자체에는 소속 탭 필드가 없어(클래스 자체 주석 참고) MemberNodeIds가 가리키는
    // 노드들의 NodeConfig.FlowId로 소속 탭을 간접 판단한다(IsGroupInFlow). Undo/Redo(EC-07 CommandHistory)
    // 대상에는 포함하지 않는다 — 탭 관리(EC-05)와 같은 이유로, 그룹 생성/접기는 캔버스 편집 커맨드
    // (노드 추가·와이어 연결·속성 편집)와 성격이 달라 별도 취급이 필요하고 완료 기준에도 Undo 요구가
    // 없어 지금은 범위 밖으로 둔다.
    private readonly Dictionary<string, GroupDefinition> _groups = new();
    private int _nextGroupSeq = 1;
    private const double GroupPadding = 14;
    private const double GroupHeaderHeight = 18;

    /// <summary>
    /// (EC-04) flows.json을 읽고 쓸 데이터 폴더 경로. Runner의 <c>Worker.cs</c>가 쓰는 것과 같은
    /// <see cref="AppContext.BaseDirectory"/>(실행 파일이 있는 폴더)를 기본값으로 둡니다 — Editor와
    /// Runner가 실제로 같은 폴더를 공유하도록 배치하는 방법(설정 파일 경로 지정 등)은 이후 Phase 8
    /// (LK-01~04) 범위의 더 깊은 배포 구성 문제라 이 Step에서는 다루지 않습니다.
    /// </summary>
    public string DataDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴). (EC-05) 초기 탭 스트립("Flow 1" 탭
    /// 1개)을 즉시 그립니다 — <see cref="LoadFlowAsync"/>는 <see cref="Loaded"/> 이후 비동기로
    /// 실행되므로, 그 전까지 화면이 비어 보이지 않도록 기본 탭을 먼저 그려둡니다.
    /// </summary>
    public FlowCanvasView()
    {
        InitializeComponent();
        RenderFlowTabStrip();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// (EC-04) 이 뷰가 화면에 처음 나타날 때(WPF <see cref="FrameworkElement.Loaded"/>) 저장된
    /// flows.json이 있으면 <see cref="LoadFlowAsync"/>로 복원합니다. <see cref="_flowLoaded"/>로
    /// 한 번만 시도하도록 막습니다(Loaded는 컨트롤이 시각 트리에서 빠졌다 다시 붙으면 다시 발생할
    /// 수 있는 이벤트라, 이미 복원했다면 다시 실행할 필요가 없음).
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_flowLoaded)
        {
            return;
        }

        _flowLoaded = true;

        // (v2.53 버그 수정) OnLoaded는 async void라 처리되지 않은 예외가 그대로 앱 전체를
        // 크래시시킨다(WPF/일반 C#의 잘 알려진 함정) — JsonWriteService.ReadAsync가 스키마 불일치를
        // 이미 내부에서 흡수하지만, 그 밖의 예상 못 한 오류(디스크 I/O 등)까지 대비해 한 번 더 감싼다.
        try
        {
            await LoadFlowAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"flows.json 불러오기 중 오류가 발생했습니다. 빈 캔버스로 시작합니다.\n{ex.Message}",
                "불러오기 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// (EC-04, EC-05 확장, EC-10 확장) <see cref="DataDirectory"/>\flows.json을 읽어 저장된 Flow 탭
    /// 목록이 있으면 기본 탭("f1", "Flow 1")을 지우고 그 목록으로 완전히 교체합니다. 모든 탭의
    /// 노드·와이어·그룹을 <see cref="_nodeConfigs"/>/<see cref="_wires"/>/<see cref="_groups"/>에
    /// 함께 채운 뒤, 노드 Id("n1", "n2"...)·탭 Id("f1", "f2"...)·그룹 Id("g1", "g2"...) 각각 가장
    /// 큰 순번 다음 값으로 <see cref="_nextNodeSeq"/>/<see cref="_nextFlowTabSeq"/>/<see cref="_nextGroupSeq"/>를
    /// 재계산해(불러온 것과 겹치지 않도록), 첫 번째 탭으로 <see cref="SwitchToFlow"/>를 호출해
    /// 화면을 그립니다.
    /// </summary>
    private async Task LoadFlowAsync()
    {
        var flows = await _flowStore.LoadAsync(DataDirectory);
        if (flows is null || flows.Count == 0)
        {
            return;
        }

        _flowTabs.Clear();
        _nodeConfigs.Clear();
        _wires.Clear();
        _groups.Clear();

        foreach (var flow in flows)
        {
            _flowTabs.Add(new FlowTabInfo(flow.Id, flow.Name));
            foreach (var node in flow.Nodes)
            {
                _nodeConfigs[node.Id] = node;
            }

            _wires.AddRange(flow.Wires);

            // (EC-10) Groups는 선택 매개변수(기본값 null)라 옛 형식(EC-05 이전) flows.json에는
            // 아예 없을 수 있음 — null이면 그룹이 없는 것과 동일하게 취급.
            if (flow.Groups is { } groups)
            {
                foreach (var group in groups)
                {
                    _groups[group.Id] = group;
                }
            }
        }

        if (_flowTabs.Count == 0)
        {
            // 이론상 flows.json이 빈 배열([])로 저장된 경우 — 기본 탭 1개로 복구해 빈 목록 상태를 피한다.
            _flowTabs.Add(new FlowTabInfo("f1", "Flow 1"));
        }

        var maxNodeSeq = 0;
        foreach (var node in _nodeConfigs.Values)
        {
            if (node.Id.StartsWith("n", StringComparison.Ordinal) &&
                int.TryParse(node.Id.AsSpan(1), out var seq) && seq > maxNodeSeq)
            {
                maxNodeSeq = seq;
            }
        }

        if (maxNodeSeq > 0)
        {
            _nextNodeSeq = maxNodeSeq + 1;
        }

        var maxTabSeq = 0;
        foreach (var tab in _flowTabs)
        {
            if (tab.Id.StartsWith("f", StringComparison.Ordinal) &&
                int.TryParse(tab.Id.AsSpan(1), out var seq) && seq > maxTabSeq)
            {
                maxTabSeq = seq;
            }
        }

        if (maxTabSeq > 0)
        {
            _nextFlowTabSeq = maxTabSeq + 1;
        }

        var maxGroupSeq = 0;
        foreach (var group in _groups.Values)
        {
            if (group.Id.StartsWith("g", StringComparison.Ordinal) &&
                int.TryParse(group.Id.AsSpan(1), out var seq) && seq > maxGroupSeq)
            {
                maxGroupSeq = seq;
            }
        }

        if (maxGroupSeq > 0)
        {
            _nextGroupSeq = maxGroupSeq + 1;
        }

        SwitchToFlow(_flowTabs[0].Id);
    }

    /// <summary>
    /// (EC-04, EC-05 확장, EC-10 확장) 지금 메모리에 있는 모든 Flow 탭의 노드·와이어·그룹을 탭별로
    /// 각각 <c>FlowDefinition</c> 하나씩으로 모아(탭에 속한 노드만 <see cref="NodeConfig.FlowId"/>로
    /// 필터링, 와이어는 양쪽 끝 노드가 모두 그 탭에 속할 때만, 그룹은 <see cref="IsGroupInFlow"/>로
    /// 판단해 포함) 목록으로 만든 뒤 <see cref="DataDirectory"/>\flows.json에 원자적으로 저장합니다
    /// (<see cref="FlowStore.SaveAsync"/>). <c>MainWindow</c>의 "파일 → 저장" 메뉴/Ctrl+S가 이
    /// 메서드를 호출합니다.
    /// </summary>
    public async Task SaveFlowAsync()
    {
        var flows = _flowTabs
            .Select(tab => new FlowDefinition(
                tab.Id,
                tab.Name,
                _nodeConfigs.Values.Where(n => n.FlowId == tab.Id).ToList(),
                _wires.Where(w => IsWireInFlow(w, tab.Id)).ToList(),
                Groups: _groups.Values.Where(g => IsGroupInFlow(g, tab.Id)).ToList()))
            .ToList();

        await _flowStore.SaveAsync(flows, DataDirectory);
    }

    /// <summary>
    /// <paramref name="wire"/>의 양쪽 끝 노드가 모두 <paramref name="flowId"/> 탭에 속하는지 확인합니다
    /// (저장 시 와이어를 어느 탭의 <c>FlowDefinition.Wires</c>에 넣을지 판단하는 용도).
    /// </summary>
    private bool IsWireInFlow(Wire wire, string flowId) =>
        _nodeConfigs.TryGetValue(wire.SourceNodeId, out var source) && source.FlowId == flowId &&
        _nodeConfigs.TryGetValue(wire.TargetNodeId, out var target) && target.FlowId == flowId;

    /// <summary>
    /// (EC-10) <paramref name="group"/>의 모든 <see cref="GroupDefinition.MemberNodeIds"/>가
    /// <paramref name="flowId"/> 탭에 속하는지 확인합니다(<see cref="GroupDefinition"/> 자체에는
    /// 소속 탭 필드가 없어 멤버 노드로 간접 판단 — 클래스 자체 주석 참고). 멤버 중 하나라도 이
    /// 탭에 없으면(다른 탭 소속이거나 노드가 이미 삭제됨) <c>false</c>를 반환합니다.
    /// </summary>
    private bool IsGroupInFlow(GroupDefinition group, string flowId) =>
        group.MemberNodeIds.All(id => _nodeConfigs.TryGetValue(id, out var node) && node.FlowId == flowId);

    /// <summary>
    /// (EC-05) 새 Flow 탭을 만들고("f{n}", "Flow {n}") 곧바로 그 탭으로 전환합니다. 탭 스트립의
    /// "＋" 버튼이 이 메서드를 호출합니다.
    /// </summary>
    private void AddFlowTab()
    {
        var id = $"f{_nextFlowTabSeq}";
        var name = $"Flow {_nextFlowTabSeq}";
        _nextFlowTabSeq++;

        _flowTabs.Add(new FlowTabInfo(id, name));
        SwitchToFlow(id);
    }

    /// <summary>
    /// (EC-05, EC-10 확장) <paramref name="flowId"/> 탭을 삭제합니다 — 이 탭에 속한 노드·와이어·
    /// 그룹이 모두 함께 삭제되므로 사용자에게 먼저 확인을 받습니다. 남은 탭이 1개뿐이면(완료 기준이
    /// "탭 3개 이상을 추가/전환/삭제해도"라 최소 1개는 항상 있어야 함) 삭제를 거부합니다. 삭제된
    /// 탭이 현재 활성 탭이었으면 남은 탭 중 첫 번째로 전환합니다.
    /// </summary>
    private void RemoveFlowTab(string flowId)
    {
        if (_flowTabs.Count <= 1)
        {
            MessageBox.Show(
                "마지막 남은 Flow 탭은 삭제할 수 없습니다.",
                "탭 삭제 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var tab = _flowTabs.FirstOrDefault(t => t.Id == flowId);
        if (tab is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"'{tab.Name}' 탭을 삭제하시겠습니까? 이 탭에 배치된 노드와 와이어가 모두 함께 삭제됩니다.",
            "Flow 탭 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var removedNodeIds = new HashSet<string>(
            _nodeConfigs.Values.Where(n => n.FlowId == flowId).Select(n => n.Id));
        foreach (var nodeId in removedNodeIds)
        {
            _nodeConfigs.Remove(nodeId);
        }

        _wires.RemoveAll(w => removedNodeIds.Contains(w.SourceNodeId) || removedNodeIds.Contains(w.TargetNodeId));

        // (EC-10) 삭제된 노드를 하나라도 포함하던 그룹은 통째로 고아가 된다(그룹의 모든 멤버는
        // 항상 같은 탭에 속한다는 전제 — 클래스 자체 주석 참고) — 남겨둬도 SaveFlowAsync가 어느
        // 탭에도 포함시키지 않아 저장은 안 되지만, 메모리에 계속 남는 죽은 데이터라 정리한다.
        var removedGroupIds = _groups
            .Where(kv => kv.Value.MemberNodeIds.Any(removedNodeIds.Contains))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var groupId in removedGroupIds)
        {
            _groups.Remove(groupId);
        }

        _flowTabs.Remove(tab);

        if (_activeFlowId == flowId)
        {
            SwitchToFlow(_flowTabs[0].Id);
        }
        else
        {
            RenderFlowTabStrip();
        }
    }

    /// <summary>
    /// (EC-05) <paramref name="flowId"/> 탭으로 전환합니다 — <see cref="RedrawActiveTab"/>로 캔버스
    /// 시각 요소를 그 탭 기준으로 다시 그리고, 탭 스트립도 새로 활성화된 탭에 맞춰 강조를 갱신합니다.
    /// </summary>
    private void SwitchToFlow(string flowId)
    {
        _activeFlowId = flowId;
        RedrawActiveTab();
        RenderFlowTabStrip();
    }

    /// <summary>
    /// (EC-07, <see cref="SwitchToFlow"/>에서 분리, EC-10 확장) <c>NodeCanvas</c>의 시각 요소(카드·포트·
    /// 와이어 선·그룹 박스)를 전부 지우고, 지금 활성 탭(<see cref="_activeFlowId"/>)에 속한 노드/와이어/
    /// 그룹을 다시 그립니다. 데이터인 <see cref="_nodeConfigs"/>/<see cref="_wires"/>/<see cref="_groups"/>는
    /// 건드리지 않습니다 — 탭 전환(<see cref="SwitchToFlow"/>)뿐 아니라 <see cref="CommandHistory"/>로
    /// 실행된 커맨드(노드 추가, 와이어 연결, 속성 편집)의 Do/Undo, 그룹 생성/접기 양쪽에서도 데이터를
    /// 바꾼 뒤 이 메서드 하나만 호출하면 화면이 항상 데이터와 일치하도록 맞출 수 있습니다(WPF 요소를
    /// 하나씩 추가/제거하는 대신 매번 새로 그리는 EC-05의 단순한 방식을 그대로 재사용). (EC-10) 접힌
    /// 그룹(<see cref="GroupDefinition.Collapsed"/>)의 소속 노드는 카드 자체를 그리지 않고(<see cref="RenderCollapsedGroup"/>이
    /// 대신 박스 하나만 그림), 그 노드에 닿는 와이어도 함께 건너뜁니다.
    /// </summary>
    private void RedrawActiveTab()
    {
        NodeCanvas.Children.Clear();
        _nodeLabels.Clear();
        _nodeVisuals.Clear();
        _dragSourcePort = null;
        _dragPreviewLine = null;
        _hoveredInputPort = null;

        // (EC-06) 카드 자체가 전부 다시 그려지므로 이전 Border 참조는 무효 — 선택 상태도 함께
        // 초기화한다(방금 Undo/Redo나 탭 전환으로 없어졌거나 다른 탭 소속이 된 노드를 계속
        // "선택됨"으로 표시할 수는 없으므로). _clipboardNode는 여기서 지우지 않는다(탭을 넘나들며
        // 붙여넣기가 계속 가능해야 하고, Undo/Redo로도 클립보드 내용이 사라질 이유가 없으므로).
        _nodeCards.Clear();
        _selectedNodeIds.Clear();

        var tabNodes = _nodeConfigs.Values.Where(n => n.FlowId == _activeFlowId).ToList();
        var tabGroups = _groups.Values.Where(g => IsGroupInFlow(g, _activeFlowId)).ToList();

        // (EC-10) 접힌 그룹의 소속 노드 Id를 모아, 카드/와이어 렌더링 양쪽에서 건너뛴다.
        var hiddenNodeIds = new HashSet<string>(
            tabGroups.Where(g => g.Collapsed).SelectMany(g => g.MemberNodeIds));

        foreach (var node in tabNodes)
        {
            if (hiddenNodeIds.Contains(node.Id))
            {
                continue;
            }

            RenderNode(node);
        }

        foreach (var wire in _wires)
        {
            if (IsWireInFlow(wire, _activeFlowId) &&
                !hiddenNodeIds.Contains(wire.SourceNodeId) && !hiddenNodeIds.Contains(wire.TargetNodeId))
            {
                var source = new PortHandle(wire.SourceNodeId, wire.SourcePort, IsOutput: true);
                var target = new PortHandle(wire.TargetNodeId, wire.TargetPort, IsOutput: false);
                DrawWireLine(source, target);
            }
        }

        foreach (var group in tabGroups)
        {
            RenderGroup(group);
        }

        EmptyCanvasHint.Visibility = tabNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RaiseSelectionChanged(); // (EC-11) 이 메서드 시작부에서 _selectedNodeIds가 비워졌으므로 Information 패널도 함께 비운다.
    }

    /// <summary>
    /// (EC-05) <see cref="_flowTabs"/>를 <c>FlowTabStrip</c>(가로 <see cref="StackPanel"/>)에 탭
    /// 버튼(이름 + "✕" 삭제 아이콘)으로 그리고, 맨 끝에 "＋"(새 탭 추가) 버튼을 붙입니다. 현재
    /// <see cref="_activeFlowId"/>인 탭은 AccentBrush 배경으로 강조합니다. <see cref="RenderNode"/>와
    /// 동일하게 데이터 템플릿 없이 즉석에서 Border+TextBlock을 만드는 코드비하인드 스타일입니다.
    /// </summary>
    private void RenderFlowTabStrip()
    {
        FlowTabStrip.Children.Clear();

        foreach (var tab in _flowTabs)
        {
            var isActive = tab.Id == _activeFlowId;

            var label = new TextBlock
            {
                Text = tab.Name,
                Foreground = (Brush)FindResource(isActive ? "PrimaryTextBrush" : "SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var closeGlyph = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = tab.Id
            };
            closeGlyph.MouseLeftButtonDown += OnFlowTabCloseClick;

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(label);
            content.Children.Add(closeGlyph);

            var tabButton = new Border
            {
                Background = (Brush)FindResource(isActive ? "AccentBrush" : "ControlBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Tag = tab.Id,
                Child = content
            };
            tabButton.MouseLeftButtonDown += OnFlowTabClick;

            FlowTabStrip.Children.Add(tabButton);
        }

        var addButton = new Border
        {
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "＋", Foreground = (Brush)FindResource("PrimaryTextBrush") }
        };
        addButton.MouseLeftButtonDown += (_, _) => AddFlowTab();

        FlowTabStrip.Children.Add(addButton);
    }

    /// <summary>(EC-05) 탭 버튼을 클릭하면 그 탭으로 전환합니다.</summary>
    private void OnFlowTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string flowId })
        {
            SwitchToFlow(flowId);
        }
    }

    /// <summary>
    /// (EC-05) 탭의 "✕" 아이콘을 클릭하면 그 탭을 삭제합니다. <paramref name="e"/>.Handled를
    /// <c>true</c>로 설정해, 이 클릭이 부모 탭 버튼의 <see cref="OnFlowTabClick"/>(탭 전환)으로
    /// 버블링되지 않도록 막습니다(삭제 확인 대화상자가 뜨기 직전에 먼저 그 탭으로 전환돼버리는
    /// 것을 방지).
    /// </summary>
    private void OnFlowTabCloseClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string flowId })
        {
            RemoveFlowTab(flowId);
        }

        e.Handled = true;
    }

    /// <summary>
    /// (EC-01b, EC-07 확장) 팔레트 카드가 <c>NodeCanvas</c> 위에 드롭되면 문자열 데이터(TypeName)를
    /// 꺼내 <see cref="NodeConfig"/>를 새로 만들고(<see cref="_nextNodeSeq"/>로 "n1", "n2"... 순번 Id
    /// 발급) <see cref="AddNodeCommand"/>로 <see cref="_history"/>에 실행합니다(Ctrl+Z로 되돌릴 수
    /// 있음). 팔레트의 "최근 사용"도 함께 갱신합니다(<see cref="PaletteView.MarkTypeUsed"/>) — 클릭뿐
    /// 아니라 실제 배치도 "사용"으로 인정합니다(단, 이건 Undo 대상이 아닌 팔레트 쪽 부가 상태라
    /// 커맨드 밖에서 처리). (EC-05) 새 노드의 <see cref="NodeConfig.FlowId"/>는 고정값이 아니라 지금
    /// 활성화된 탭(<see cref="_activeFlowId"/>)입니다 — 사용자가 보고 있는 탭에 정확히 배치됩니다.
    /// </summary>
    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.StringFormat) is not string typeName || typeName.Length == 0)
        {
            return;
        }

        var position = e.GetPosition(NodeCanvas);
        var config = new NodeConfig(
            Id: $"n{_nextNodeSeq++}",
            Type: typeName,
            Name: typeName,
            FlowId: _activeFlowId,
            Properties: new Dictionary<string, object?>(),
            X: position.X,
            Y: position.Y);

        _history.Execute(new AddNodeCommand(this, config));
        Palette.MarkTypeUsed(typeName);
    }

    /// <summary>
    /// (EC-01b~EC-02, EC-08 확장) <paramref name="config"/>를 나타내는 작은 카드(Border+TextBlock)를
    /// <see cref="NodeConfig.X"/>/<see cref="NodeConfig.Y"/> 중심으로 <c>NodeCanvas</c>에 추가하고,
    /// 좌우에 입력/출력 포트 Ellipse를 붙입니다(<see cref="AddPortEllipse"/>). 카드 크기가 고정이라
    /// (<see cref="NodeCardWidth"/>/<see cref="NodeCardHeight"/>) WPF 레이아웃 측정을 기다리지
    /// 않고도 포트 좌표를 바로 계산할 수 있습니다. (EC-05) 이 메서드는 <paramref name="config"/>.FlowId가
    /// 현재 활성 탭인지 확인하지 않습니다 — 호출부(<see cref="OnCanvasDrop"/>/<see cref="RedrawActiveTab"/>)가
    /// 항상 활성 탭에 속한 노드만 넘겨준다는 것을 전제로 합니다. (EC-08) <paramref name="config"/>.Type이
    /// <see cref="_registry"/>에 등록돼 있지 않으면(RT-02a <c>MissingNode</c>와 동일한 판정 —
    /// Editor는 Runner와 별도 프로세스라 실제 배포 결과 대신 이 뷰의 자체 <c>NodeTypeRegistry</c>로
    /// "타입을 찾을 수 없음"을 독립적으로 판단합니다) 제목을 "⚠ {Type}"으로, 부제목을 "missing type"으로
    /// 바꿔 표시하고(12번 탭 카드2 목업 <c>mock-node err</c>와 동일한 문구), 테두리는
    /// <see cref="ApplyCardBorder"/>가 <c>RedBrush</c>로 강조합니다.
    /// </summary>
    private void RenderNode(NodeConfig config)
    {
        var left = Math.Max(0, config.X - NodeCardWidth / 2);
        var top = Math.Max(0, config.Y - NodeCardHeight / 2);

        // (EC-08) Editor 자체 레지스트리 기준의 "타입 없음" 판정 — RT-02a의 MissingNode와 같은 개념을
        // Runner 배포 결과를 기다리지 않고 Editor 쪽에서 독립적으로 판단한다(위 클래스 주석 참고).
        var isMissing = !_registry.Descriptors.ContainsKey(config.Type);

        var label = new TextBlock
        {
            Text = isMissing ? $"⚠ {config.Type}" : config.Name,
            Foreground = (Brush)FindResource(isMissing ? "RedBrush" : "PrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 4, 4, isMissing ? 0 : 4)
        };

        // (EC-08) 알 수 없는 타입일 때만 "missing type" 부제목을 라벨 아래에 추가한다(12번 탭 카드2
        // 목업 mock-node의 .t/.s 두 줄 구성과 동일) — 정상 노드는 기존과 동일하게 라벨 한 줄만 표시.
        FrameworkElement cardContent = label;
        if (isMissing)
        {
            var subtitle = new TextBlock
            {
                Text = "missing type",
                FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 2)
            };
            cardContent = new StackPanel { Children = { label, subtitle } };
        }

        // (EC-11) config.Description이 채워져 있으면 카드 우측 상단에 문서 배지를 겹쳐 그린다 — 라벨
        // 한 줄/missing type 두 줄 구성 그 자체는 그대로 두고, Grid로 감싸 배지만 덧붙이는 방식이라
        // 기존 레이아웃(위 두 케이스)에 영향을 주지 않는다.
        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            var badge = new TextBlock
            {
                Text = "📝",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = "설명 보기"
            };
            var description = config.Description;
            badge.MouseLeftButtonDown += (_, e) =>
            {
                MessageBox.Show(description, $"{config.Name} — 설명", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Handled = true; // 카드 자체의 선택/더블클릭 처리로 버블링되지 않게 막는다.
            };

            var overlay = new Grid();
            overlay.Children.Add(cardContent);
            overlay.Children.Add(badge);
            cardContent = overlay;
        }

        var card = new Border
        {
            Width = NodeCardWidth,
            Height = NodeCardHeight,
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Arrow,
            Tag = config.Id,
            Child = cardContent
        };
        card.MouseLeftButtonDown += OnCardMouseLeftButtonDown;

        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        NodeCanvas.Children.Add(card);
        _nodeLabels[config.Id] = label;
        _nodeCards[config.Id] = card; // (EC-06) 선택 시 테두리 강조를 위해 Border 참조를 보관
        ApplyCardBorder(config.Id, card); // (EC-08) 최초 테두리도 선택/누락 상태에 맞춰 설정

        // EC-02 범위: 지금은 모든 노드를 입력 1개·출력 1개로 고정한다(클래스 주석 참고 — Phase 7
        // 이후 실제 노드 타입의 DefaultInputs/DefaultOutputs를 반영할 예정, MissingNode의 실제
        // 포트 0개(위 클래스 주석) 반영도 같은 Phase 7 이후 범위).
        const int inputs = 1;
        const int outputs = 1;

        var visual = new PlacedNodeVisual(config.Id, left, top, NodeCardWidth, NodeCardHeight, inputs, outputs);
        _nodeVisuals[config.Id] = visual;

        for (var i = 0; i < inputs; i++)
        {
            AddPortEllipse(new PortHandle(config.Id, i, IsOutput: false), visual.GetInputPortPosition(i));
        }

        for (var i = 0; i < outputs; i++)
        {
            AddPortEllipse(new PortHandle(config.Id, i, IsOutput: true), visual.GetOutputPortPosition(i));
        }
    }

    /// <summary>
    /// <paramref name="handle"/>이 나타내는 포트 하나를 <paramref name="center"/> 위치에 작은 원으로
    /// 그립니다. 출력 포트는 <see cref="OnOutputPortMouseDown"/>로 와이어 드래그를 시작하고, 입력
    /// 포트는 <see cref="_hoveredInputPort"/>를 갱신해 "지금 이 포트 위에 마우스가 있다"는 것만
    /// 기억합니다(실제 Wire 생성 판정은 <see cref="OnCanvasMouseUp"/>에서 이 값을 읽어 처리).
    /// </summary>
    private void AddPortEllipse(PortHandle handle, Point center)
    {
        var ellipse = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Fill = (Brush)FindResource("AccentBrush"),
            Tag = handle,
            Cursor = Cursors.Hand
        };

        Canvas.SetLeft(ellipse, center.X - PortRadius);
        Canvas.SetTop(ellipse, center.Y - PortRadius);
        Panel.SetZIndex(ellipse, 1);
        NodeCanvas.Children.Add(ellipse);

        if (handle.IsOutput)
        {
            ellipse.MouseLeftButtonDown += OnOutputPortMouseDown;
        }
        else
        {
            ellipse.MouseEnter += (_, _) => _hoveredInputPort = handle;
            ellipse.MouseLeave += (_, _) =>
            {
                if (Equals(_hoveredInputPort, handle))
                {
                    _hoveredInputPort = null;
                }
            };
        }
    }

    /// <summary>
    /// (EC-02) 출력 포트를 누르면 와이어 드래그를 시작합니다 — 시작점을 기억하고, 미리보기용
    /// <see cref="Line"/>을 캔버스에 추가한 뒤 <see cref="Mouse.Capture(System.Windows.IInputElement)"/>로
    /// 이후 마우스 이벤트를 전부 <c>NodeCanvas</c>가 받도록 만듭니다(포트 Ellipse 밖으로 마우스가
    /// 나가도 드래그가 끊기지 않게 하기 위함).
    /// </summary>
    private void OnOutputPortMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Ellipse { Tag: PortHandle handle })
        {
            return;
        }

        _dragSourcePort = handle;

        var start = _nodeVisuals[handle.NodeId].GetOutputPortPosition(handle.PortIndex);
        _dragPreviewLine = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = start.X,
            Y2 = start.Y,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2
        };
        NodeCanvas.Children.Add(_dragPreviewLine);

        Mouse.Capture(NodeCanvas);
        e.Handled = true;
    }

    /// <summary>와이어 드래그 중이면 미리보기 선의 끝점을 현재 마우스 위치로 계속 갱신합니다.</summary>
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPreviewLine is null)
        {
            return;
        }

        var current = e.GetPosition(NodeCanvas);
        _dragPreviewLine.X2 = current.X;
        _dragPreviewLine.Y2 = current.Y;
    }

    /// <summary>
    /// (EC-02, EC-07 확장) 마우스를 놓으면 드래그를 끝냅니다. <see cref="_hoveredInputPort"/>가 다른
    /// 노드의 입력 포트를 가리키고 있으면 <see cref="Wire"/>를 만들어 <see cref="AddWireCommand"/>로
    /// <see cref="_history"/>에 실행합니다(Ctrl+Z로 되돌릴 수 있음), 포트 영역 밖(또는 자기 자신)에서
    /// 놓으면 미리보기 선만 지우고 아무 것도 만들지 않습니다(완료 기준의 "포트 영역 밖에서 드롭하면
    /// 생성되지 않는지" 조건).
    /// </summary>
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragSourcePort is not { } source || _dragPreviewLine is null)
        {
            return;
        }

        NodeCanvas.Children.Remove(_dragPreviewLine);
        _dragPreviewLine = null;
        Mouse.Capture(null);

        if (_hoveredInputPort is { } target && target.NodeId != source.NodeId)
        {
            var wire = new Wire(source.NodeId, source.PortIndex, target.NodeId, target.PortIndex);
            _history.Execute(new AddWireCommand(this, wire));
        }

        _dragSourcePort = null;
    }

    /// <summary><paramref name="source"/>→<paramref name="target"/> 사이에 실선을 그려 완성된 연결을 표시합니다.</summary>
    private void DrawWireLine(PortHandle source, PortHandle target)
    {
        var start = _nodeVisuals[source.NodeId].GetOutputPortPosition(source.PortIndex);
        var end = _nodeVisuals[target.NodeId].GetInputPortPosition(target.PortIndex);

        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = (Brush)FindResource("PrimaryTextBrush"),
            StrokeThickness = 2
        };

        // 새 와이어 선이 나중에 그려진 노드 카드 위를 덮지 않도록 맨 뒤(카드보다 아래)로 보낸다.
        Panel.SetZIndex(line, -1);
        NodeCanvas.Children.Insert(0, line);
    }

    /// <summary>
    /// (EC-03, EC-06 확장, EC-10 확장) 카드를 더블클릭(<c>e.ClickCount == 2</c>)하면 그 카드의
    /// Tag(NodeId)로 <see cref="OpenPropertyDialog"/>를 엽니다. 한 번 클릭이면 Ctrl(<see cref="ModifierKeys.Control"/>)이
    /// 눌려 있는지로 갈립니다 — (EC-10) Ctrl+클릭은 <see cref="ToggleNodeSelection"/>으로 그 노드만
    /// 선택 목록에 추가/제거(여러 노드를 모아 <see cref="GroupSelectedNodes"/>로 그룹 묶기), (EC-06)
    /// Ctrl 없는 일반 클릭은 <see cref="SelectNode"/>로 "이 노드 하나만" 선택(다른 선택은 모두
    /// 해제). 어느 경우든 <paramref name="e"/>.Handled를 <c>true</c>로 설정해, 이 클릭이
    /// <see cref="NodeCanvas"/>의 배경 클릭 핸들러(<see cref="OnCanvasBackgroundMouseDown"/>)로
    /// 버블링되어 방금 한 선택이 곧바로 해제되는 것을 막습니다.
    /// </summary>
    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string nodeId })
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            OpenPropertyDialog(nodeId);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ToggleNodeSelection(nodeId);
        }
        else
        {
            SelectNode(nodeId);
        }

        e.Handled = true;
    }

    /// <summary>
    /// (EC-06, EC-08 확장, EC-10 확장) <paramref name="nodeId"/> 하나만 선택 상태로 만듭니다(다른
    /// 선택은 모두 해제) — <c>null</c>이면 선택을 전부 해제합니다. <see cref="ApplyCardBorder"/>로
    /// 영향받은 카드(이전에 선택돼 있던 것 + 새로 선택된 것)의 테두리를 그 상태에 맞게 다시
    /// 칠합니다. <see cref="_nodeCards"/>에 없는 Id(이미 지워졌거나 다른 탭 소속)가 들어오면 그
    /// 카드에 대한 강조만 건너뜁니다. 여러 노드를 함께 선택하려면(그룹 묶기용) Ctrl+클릭으로
    /// <see cref="ToggleNodeSelection"/>을 대신 쓰십시오.
    /// </summary>
    private void SelectNode(string? nodeId)
    {
        var affectedIds = new HashSet<string>(_selectedNodeIds);
        if (nodeId is not null)
        {
            affectedIds.Add(nodeId);
        }

        _selectedNodeIds.Clear();
        if (nodeId is not null)
        {
            _selectedNodeIds.Add(nodeId);
        }

        foreach (var id in affectedIds)
        {
            if (_nodeCards.TryGetValue(id, out var card))
            {
                ApplyCardBorder(id, card);
            }
        }

        RaiseSelectionChanged(); // (EC-11) Information 패널 갱신
    }

    /// <summary>
    /// (EC-10) <paramref name="nodeId"/>를 현재 선택 목록(<see cref="_selectedNodeIds"/>)에
    /// 추가하거나(아직 없으면) 제거합니다(이미 있으면) — 다른 노드의 선택 상태는 건드리지 않습니다.
    /// Ctrl+클릭(<see cref="OnCardMouseLeftButtonDown"/>)이 이 메서드를 호출해 여러 노드를 한 번에
    /// 골라 <see cref="GroupSelectedNodes"/>/<see cref="UngroupSelectedGroup"/>의 대상으로 삼습니다.
    /// </summary>
    private void ToggleNodeSelection(string nodeId)
    {
        if (!_selectedNodeIds.Remove(nodeId))
        {
            _selectedNodeIds.Add(nodeId);
        }

        if (_nodeCards.TryGetValue(nodeId, out var card))
        {
            ApplyCardBorder(nodeId, card);
        }

        RaiseSelectionChanged(); // (EC-11) Information 패널 갱신
    }

    /// <summary>
    /// (EC-08, EC-10 확장) <paramref name="card"/>의 테두리를 상태 우선순위(선택 &gt; 알 수 없는
    /// 타입 &gt; 기본)에 따라 정합니다 — <see cref="RenderNode"/>(최초 렌더링)와
    /// <see cref="SelectNode"/>/<see cref="ToggleNodeSelection"/>(선택 변경) 모두가 이 메서드
    /// 하나를 공유해 테두리 규칙이 한 곳에만 있도록 합니다. <paramref name="nodeId"/>가
    /// <see cref="_selectedNodeIds"/>에 있으면 무조건 <c>AccentBrush</c>/두께 2로 강조하고, 아니면
    /// <see cref="NodeConfig.Type"/>이 <see cref="_registry"/>에 없을 때(EC-08 "누락 노드")
    /// <c>RedBrush</c>/두께 2로, 그 외에는 기본 <c>BorderBrush</c>/두께 1로 되돌립니다.
    /// </summary>
    private void ApplyCardBorder(string nodeId, Border card)
    {
        if (_selectedNodeIds.Contains(nodeId))
        {
            card.BorderBrush = (Brush)FindResource("AccentBrush");
            card.BorderThickness = new Thickness(2);
            return;
        }

        var isMissing = _nodeConfigs.TryGetValue(nodeId, out var config) && !_registry.Descriptors.ContainsKey(config.Type);
        if (isMissing)
        {
            card.BorderBrush = (Brush)FindResource("RedBrush");
            card.BorderThickness = new Thickness(2);
        }
        else
        {
            card.BorderBrush = (Brush)FindResource("BorderBrush");
            card.BorderThickness = new Thickness(1);
        }
    }

    /// <summary>
    /// (EC-06) <see cref="NodeCanvas"/>의 빈 배경(카드·포트가 아닌 영역)을 클릭하면 선택을 모두
    /// 해제합니다. 카드 클릭(<see cref="OnCardMouseLeftButtonDown"/>)과 포트 클릭
    /// (<see cref="OnOutputPortMouseDown"/>)은 각자 <c>e.Handled = true</c>로 이 핸들러까지
    /// 버블링되지 않도록 이미 막고 있으므로, 이 핸들러는 정말 빈 배경을 눌렀을 때만 실행됩니다.
    /// </summary>
    private void OnCanvasBackgroundMouseDown(object sender, MouseButtonEventArgs e) => SelectNode(null);

    /// <summary>
    /// (EC-06, EC-10 확장) 지금 정확히 노드 하나만 선택돼 있으면 그 <see cref="NodeConfig"/>를 내부
    /// 클립보드(<see cref="_clipboardNode"/>)에 복사합니다. <see cref="NodeConfig.Properties"/>는
    /// 참조 타입(Dictionary)이라 그대로 담으면 원본과 클립보드가 같은 인스턴스를 공유하게 되므로,
    /// 새 Dictionary로 복제해 독립시킵니다(<see cref="NodeConfig"/> 자체 XML 문서의 "record 동등성
    /// 주의"와 같은 이유). 선택된 노드가 없거나(EC-06 원래 동작) 2개 이상 선택돼 있으면(EC-10으로
    /// 새로 가능해진 다중 선택 — 복사 대상이 모호해 여러 노드 복사는 이번 Step 범위에 넣지 않음)
    /// 아무 것도 하지 않습니다 — <c>MainWindow</c>의 Ctrl+C/"편집 → 복사"가 이 메서드를 호출합니다.
    /// </summary>
    public void CopySelectedNode()
    {
        if (_selectedNodeIds.Count != 1)
        {
            return;
        }

        var nodeId = _selectedNodeIds.First();
        if (!_nodeConfigs.TryGetValue(nodeId, out var config))
        {
            return;
        }

        _clipboardNode = config with { Properties = new Dictionary<string, object?>(config.Properties) };
    }

    /// <summary>
    /// (EC-06, EC-07 확장) 내부 클립보드(<see cref="_clipboardNode"/>)에 담긴 노드를 새 Id로
    /// 재발급해(<see cref="_nextNodeSeq"/>, 원본과 절대 겹치지 않음) 지금 활성 탭
    /// (<see cref="_activeFlowId"/>)에 <see cref="AddNodeCommand"/>로 붙여넣습니다(Ctrl+Z로 되돌릴
    /// 수 있음). 원본 카드 위에 완전히 겹쳐 보이지 않도록 좌표를 <see cref="PasteOffset"/>만큼
    /// 대각선으로 어긋나게 놓고, 붙여넣은 새 노드를 곧바로 <see cref="SelectNode"/>로 선택 상태로
    /// 만듭니다(연속으로 Ctrl+V를 누르면 매번 조금씩 어긋난 위치에 새 사본이 쌓이는 자연스러운
    /// 동작 — 선택 상태 자체는 Undo 대상이 아니라 커맨드 실행 뒤에 별도로 적용). 클립보드가
    /// 비어있으면(아직 복사한 적 없음) 아무 것도 하지 않습니다 — <c>MainWindow</c>의 Ctrl+V/
    /// "편집 → 붙여넣기"가 이 메서드를 호출합니다.
    /// </summary>
    public void PasteNode()
    {
        if (_clipboardNode is not { } source)
        {
            return;
        }

        var pasted = source with
        {
            Id = $"n{_nextNodeSeq++}",
            FlowId = _activeFlowId,
            X = source.X + PasteOffset,
            Y = source.Y + PasteOffset,
            Properties = new Dictionary<string, object?>(source.Properties)
        };

        _history.Execute(new AddNodeCommand(this, pasted));
        SelectNode(pasted.Id);
    }

    /// <summary>
    /// (EC-03, EC-07 확장) <paramref name="nodeId"/>의 현재 <see cref="NodeConfig"/>와,
    /// <see cref="_registry"/>에 등록된 해당 타입의 PropertySchema(없으면 빈 목록 — Phase 7
    /// 이전엔 항상 이 경우)로 <see cref="NodePropertyDialog"/>를 모달로 띄웁니다. "완료"로 닫히면
    /// 편집 전/후 <see cref="NodeConfig"/> 스냅샷을 <see cref="EditNodePropertiesCommand"/>로 감싸
    /// <see cref="_history"/>에 실행합니다(Ctrl+Z로 되돌릴 수 있음) — 화면 갱신(카드 이름 표시 등)은
    /// 커맨드가 호출하는 <see cref="RedrawActiveTab"/>이 처리합니다.
    /// </summary>
    private void OpenPropertyDialog(string nodeId)
    {
        if (!_nodeConfigs.TryGetValue(nodeId, out var config))
        {
            return;
        }

        var schema = _registry.Descriptors.TryGetValue(config.Type, out var descriptor)
            ? descriptor.PropertySchema
            : Array.Empty<PropertyField>();

        var dialog = new NodePropertyDialog(config, schema)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.UpdatedConfig is { } updated)
        {
            _history.Execute(new EditNodePropertiesCommand(this, nodeId, before: config, after: updated));
        }
    }

    /// <summary>
    /// (EC-07) Ctrl+Z(<c>MainWindow</c>의 <c>ApplicationCommands.Undo</c>)로 <see cref="_history"/>의
    /// 가장 최근 커맨드(노드 추가/와이어 연결/속성 편집 중 하나)를 되돌립니다. 되돌릴 커맨드가
    /// 없으면 아무 것도 하지 않습니다.
    /// </summary>
    public void Undo() => _history.Undo();

    /// <summary>
    /// (EC-07) Ctrl+Y(<c>MainWindow</c>의 <c>ApplicationCommands.Redo</c>)로 <see cref="_history"/>가
    /// Undo로 되돌렸던 커맨드를 다시 실행합니다. 다시 실행할 커맨드가 없으면 아무 것도 하지 않습니다.
    /// </summary>
    public void Redo() => _history.Redo();

    /// <summary>(EC-07) <c>MainWindow</c>가 "편집 → 실행 취소"/Ctrl+Z의 활성화 여부를 판단하는 데 씁니다.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>(EC-07) <c>MainWindow</c>가 "편집 → 다시 실행"/Ctrl+Y의 활성화 여부를 판단하는 데 씁니다.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>
    /// (EC-10) 지금 Ctrl+클릭으로 선택된 노드 2개 이상을(<see cref="_selectedNodeIds"/>) 새
    /// <see cref="GroupDefinition"/>으로 묶습니다("g{n}" Id, "Group {n}" 이름 자동 발급). 완료
    /// 기준의 검증 시나리오는 노드 3개지만, 그룹 자체는 2개부터도 의미가 있어 최소 인원을 2개로
    /// 판단했습니다(낮은 리스크). 선택이 1개 이하면 아무 것도 하지 않습니다(그룹의 의미가 없음).
    /// 그룹을 만든 뒤에는 개별 노드 선택을 해제합니다(그룹 박스가 새로 생겼으니 낱개 카드 선택
    /// 상태를 유지할 이유가 없음). <c>MainWindow</c>의 Ctrl+G/"편집 → 그룹으로 묶기"가 이 메서드를
    /// 호출합니다.
    /// </summary>
    public void GroupSelectedNodes()
    {
        if (_selectedNodeIds.Count < 2)
        {
            return;
        }

        var id = $"g{_nextGroupSeq}";
        var group = new GroupDefinition(
            Id: id,
            Name: $"Group {_nextGroupSeq}",
            MemberNodeIds: _selectedNodeIds.ToList());
        _nextGroupSeq++;

        _groups[id] = group;
        SelectNode(null);
        RedrawActiveTab();
    }

    /// <summary>
    /// (EC-10) 지금 선택된 노드가 속한 그룹을 전부 찾아 해제합니다 — 그룹 자체를 직접 선택하는
    /// UI가 따로 없어(캔버스에는 노드 선택만 있음), "이 그룹에 속한 노드 하나를 골라 Ctrl+Shift+G"를
    /// 트리거로 재사용하는 가장 단순한 방법을 택했습니다(완료 기준에는 없지만, 그룹을 만들기만
    /// 하고 되돌릴 방법이 없으면 실사용이 어려워 Node-RED의 Ctrl+Shift+G 관례를 따라 낮은 리스크로
    /// 함께 추가). 그룹 자체는 삭제되지만 소속 노드·와이어는 그대로 남습니다(캔버스 표시 전용
    /// 요소를 해제하는 것뿐, 데이터 손실 없음). 선택된 노드가 없거나 어떤 그룹에도 속하지 않으면
    /// 아무 것도 하지 않습니다 — <c>MainWindow</c>의 Ctrl+Shift+G/"편집 → 그룹 해제"가 이 메서드를
    /// 호출합니다.
    /// </summary>
    public void UngroupSelectedGroup()
    {
        if (_selectedNodeIds.Count == 0)
        {
            return;
        }

        var groupIdsToRemove = _groups.Values
            .Where(g => g.MemberNodeIds.Any(_selectedNodeIds.Contains))
            .Select(g => g.Id)
            .ToList();

        if (groupIdsToRemove.Count == 0)
        {
            return;
        }

        foreach (var groupId in groupIdsToRemove)
        {
            _groups.Remove(groupId);
        }

        RedrawActiveTab();
    }

    /// <summary>
    /// (EC-12) 모든 Flow 탭(EC-05)에 걸쳐 노드 이름(<see cref="NodeConfig.Name"/>) 또는 속성 값
    /// (<see cref="NodeConfig.Properties"/>)에 <paramref name="query"/>가 대소문자 구분 없이 포함된
    /// 노드를 찾습니다 — Ctrl+F(<c>MainWindow</c>)로 Explorer 패널이 이 메서드를 호출합니다.
    /// <paramref name="query"/>가 비어 있으면 빈 목록을 돌려줍니다(검색창이 비었을 때 노드 전체를
    /// 나열하지 않도록). Properties 값은 flows.json에서 막 불러온 직후엔
    /// <see cref="System.Text.Json.JsonElement"/>, 방금 편집한 직후엔 일반 <c>string</c>일 수 있어(
    /// <see cref="NodeConfig"/> 자체 문서의 "Properties 역직렬화 주의" 참고) 타입을 가리지 않고
    /// <c>ToString()</c>으로 통일해 비교합니다 — Ctrl+F는 Node-RED에서도 필드 타입을 구분하지 않는
    /// 단순 텍스트 검색이라 이 정도 정밀도로 충분하다고 판단했습니다.
    /// </summary>
    public IReadOnlyList<NodeSearchResult> SearchNodes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<NodeSearchResult>();
        }

        var results = new List<NodeSearchResult>();
        foreach (var node in _nodeConfigs.Values)
        {
            var nameMatch = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            var propertyMatch = node.Properties.Values.Any(value =>
                value is not null && value.ToString()!.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (!nameMatch && !propertyMatch)
            {
                continue;
            }

            var flowName = _flowTabs.FirstOrDefault(tab => tab.Id == node.FlowId)?.Name ?? node.FlowId;
            results.Add(new NodeSearchResult(node.FlowId, flowName, node.Id, node.Name, node.Type));
        }

        return results;
    }

    /// <summary>
    /// (EC-12) Explorer 패널의 검색 결과 하나를 클릭했을 때 호출됩니다 — <paramref name="nodeId"/>가
    /// 속한 Flow 탭으로 전환하고(다른 탭이면 <see cref="SwitchToFlow"/>), 그 노드가 접힌 그룹
    /// (<see cref="GroupDefinition.Collapsed"/>) 소속이면 카드가 실제로 보이도록 먼저 펼친 뒤,
    /// <see cref="SelectNode"/>로 선택 상태(<c>AccentBrush</c> 테두리)를 줘 완료 기준의 "노드가
    /// 하이라이트"를 만족시킵니다. <paramref name="nodeId"/>가 이미 삭제됐거나 존재하지 않으면 아무
    /// 것도 하지 않습니다.
    /// </summary>
    public void NavigateToNode(string flowId, string nodeId)
    {
        if (!_nodeConfigs.ContainsKey(nodeId))
        {
            return;
        }

        if (_activeFlowId != flowId)
        {
            SwitchToFlow(flowId);
        }

        // 접힌 그룹 소속이면 먼저 펼친다 — 그렇지 않으면 RedrawActiveTab이 그 카드 자체를 그리지
        // 않아(EC-10) SelectNode로 선택해도 화면에 아무 것도 강조되지 않는다.
        var containingGroup = _groups.Values.FirstOrDefault(g => g.Collapsed && g.MemberNodeIds.Contains(nodeId));
        if (containingGroup is not null)
        {
            _groups[containingGroup.Id] = containingGroup with { Collapsed = false };
            RedrawActiveTab();
        }

        SelectNode(nodeId);
    }

    /// <summary>
    /// (EC-10) <paramref name="group"/>을 <see cref="GroupDefinition.Collapsed"/> 여부에 따라
    /// <see cref="RenderExpandedGroupBox"/> 또는 <see cref="RenderCollapsedGroup"/>으로 그립니다 —
    /// <see cref="RedrawActiveTab"/>이 노드·와이어를 다 그린 뒤 각 그룹마다 이 메서드를 호출합니다.
    /// </summary>
    private void RenderGroup(GroupDefinition group)
    {
        if (group.Collapsed)
        {
            RenderCollapsedGroup(group);
        }
        else
        {
            RenderExpandedGroupBox(group);
        }
    }

    /// <summary>
    /// (EC-10) 펼쳐진 그룹을 소속 노드 카드들을 감싸는 사각형 박스로 그립니다 — 이미 그려진
    /// <see cref="_nodeVisuals"/>(멤버 카드들의 위치·크기)의 최소/최대 좌표에
    /// <see cref="GroupPadding"/>만큼 여백을 둔 바운딩 박스를 계산하고, 위쪽에
    /// <see cref="GroupHeaderHeight"/>만큼 이름 표시 공간을 더 둡니다. 박스는 채우기 없이 테두리만
    /// 그려(카드를 가리지 않도록) 카드보다 뒤(<c>ZIndex -3</c>, 와이어의 -1보다도 더 뒤)에 배치하고,
    /// 이름+접기 글리프(<see cref="BuildGroupHeader"/>)는 카드보다 앞(<c>ZIndex 2</c>)에 둬 항상
    /// 클릭 가능하게 합니다. 이 탭에 실제로 그려진 멤버가 하나도 없으면(다른 탭 소속이거나 노드가
    /// 삭제됨 — <see cref="IsGroupInFlow"/>가 이미 걸러내므로 이론상 발생하지 않지만 방어적으로)
    /// 아무 것도 그리지 않습니다.
    /// </summary>
    private void RenderExpandedGroupBox(GroupDefinition group)
    {
        var memberVisuals = group.MemberNodeIds
            .Select(id => _nodeVisuals.TryGetValue(id, out var visual) ? visual : null)
            .Where(visual => visual is not null)
            .Select(visual => visual!)
            .ToList();

        if (memberVisuals.Count == 0)
        {
            return;
        }

        var left = memberVisuals.Min(v => v.Left) - GroupPadding;
        var top = memberVisuals.Min(v => v.Top) - GroupPadding - GroupHeaderHeight;
        var right = memberVisuals.Max(v => v.Left + v.Width) + GroupPadding;
        var bottom = memberVisuals.Max(v => v.Top + v.Height) + GroupPadding;

        var box = new Border
        {
            Width = Math.Max(0, right - left),
            Height = Math.Max(0, bottom - top),
            Background = Brushes.Transparent,
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6)
        };
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        Panel.SetZIndex(box, -3);
        NodeCanvas.Children.Add(box);

        var header = BuildGroupHeader(group, collapsed: false);
        Canvas.SetLeft(header, left + 6);
        Canvas.SetTop(header, top + 1);
        Panel.SetZIndex(header, 2);
        NodeCanvas.Children.Add(header);
    }

    /// <summary>
    /// (EC-10) 접힌 그룹을 소속 노드 카드 대신 박스 하나로 축약 표시합니다(완료 기준 "접으면 박스
    /// 하나로 축약 표시"). 멤버 노드는 <see cref="RedrawActiveTab"/>이 이미 카드로 그리지 않았으므로
    /// (<c>hiddenNodeIds</c>) <see cref="_nodeVisuals"/>에 위치 정보가 없습니다 — 대신 멤버들의
    /// <see cref="NodeConfig.X"/>/<see cref="NodeConfig.Y"/> 평균(centroid)을 박스 중심으로 씁니다
    /// (그룹 자체에 별도 좌표 필드가 없어 택한 가장 단순한 방법 — 펼쳤을 때의 정확한 배치를 별도로
    /// 기억하려면 <see cref="GroupDefinition"/>에 좌표 필드가 더 필요하지만, 완료 기준은 "저장 후
    /// 재로드해도 접힘 상태가 유지되고 박스 하나로 축약 표시"까지만 요구해 이 수준으로 충분하다고
    /// 판단). 박스를 클릭하면 <see cref="OnGroupHeaderClick"/>이 펼칩니다.
    /// </summary>
    private void RenderCollapsedGroup(GroupDefinition group)
    {
        var members = group.MemberNodeIds
            .Select(id => _nodeConfigs.TryGetValue(id, out var node) ? node : null)
            .Where(node => node is not null)
            .Select(node => node!)
            .ToList();

        if (members.Count == 0)
        {
            return;
        }

        var centerX = members.Average(n => n.X);
        var centerY = members.Average(n => n.Y);
        const double width = NodeCardWidth + 40;
        const double height = NodeCardHeight;

        var box = new Border
        {
            Width = width,
            Height = height,
            Background = (Brush)FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Tag = group.Id,
            Child = BuildGroupHeader(group, collapsed: true)
        };
        box.MouseLeftButtonDown += OnGroupHeaderClick;

        Canvas.SetLeft(box, centerX - width / 2);
        Canvas.SetTop(box, centerY - height / 2);
        NodeCanvas.Children.Add(box);
    }

    /// <summary>
    /// (EC-10) 그룹 이름·접기 글리프(펼침 "▼"/접힘 "▶")·소속 노드 개수를 담은
    /// <see cref="TextBlock"/>을 만듭니다. <paramref name="collapsed"/>가 <c>false</c>면(펼친 상태)
    /// 이 텍스트 자체에 <see cref="OnGroupHeaderClick"/>을 연결합니다 — 접힌 상태의 클릭은
    /// <see cref="RenderCollapsedGroup"/>이 바깥 박스에 이미 연결해뒀으므로, 여기서 또 연결하면
    /// 클릭 한 번에 두 번 실행되는 것을 피하기 위해 조건부로만 연결합니다.
    /// </summary>
    private TextBlock BuildGroupHeader(GroupDefinition group, bool collapsed)
    {
        var glyph = collapsed ? "▶" : "▼";
        var header = new TextBlock
        {
            Text = $"{glyph} {group.Name} ({group.MemberNodeIds.Count})",
            FontSize = 10,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = collapsed ? VerticalAlignment.Center : VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            Tag = group.Id
        };

        if (!collapsed)
        {
            header.MouseLeftButtonDown += OnGroupHeaderClick;
        }

        return header;
    }

    /// <summary>
    /// (EC-10) 그룹 헤더(펼친 상태) 또는 접힌 박스를 클릭하면 그 그룹의
    /// <see cref="GroupDefinition.Collapsed"/>를 반전시켜(<c>with</c> 식으로 새 인스턴스 교체 —
    /// <see cref="GroupDefinition"/>이 불변 record) <see cref="RedrawActiveTab"/>으로 다시 그립니다.
    /// <paramref name="e"/>.Handled를 <c>true</c>로 설정해 <see cref="OnCanvasBackgroundMouseDown"/>으로
    /// 버블링되지 않게 막습니다(카드 클릭과 동일한 이유).
    /// </summary>
    private void OnGroupHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string groupId } && _groups.TryGetValue(groupId, out var group))
        {
            _groups[groupId] = group with { Collapsed = !group.Collapsed };
            RedrawActiveTab();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Class명 : 노드 추가 커맨드
    /// 역활 및 기능 : 새 NodeConfig를 캔버스에 추가하는 Undo/Redo 가능한 커맨드
    ///
    /// 팔레트 드롭(<see cref="OnCanvasDrop"/>)과 붙여넣기(<see cref="PasteNode"/>) 양쪽이 공유하는
    /// 커맨드입니다 — 둘 다 "새 NodeConfig 하나를 <see cref="_nodeConfigs"/>에 추가하고 화면을
    /// 갱신"이라는 같은 동작이라, 새 노드가 어디서 왔는지(팔레트 vs 클립보드)와 무관하게 이 커맨드
    /// 하나로 처리합니다. 이 클래스는 <see cref="FlowCanvasView"/>의 중첩 클래스라 <c>_owner</c>를
    /// 통해 바깥 클래스의 private 필드(<see cref="_nodeConfigs"/> 등)에 직접 접근합니다.
    /// </summary>
    private sealed class AddNodeCommand : IEditorCommand
    {
        private readonly FlowCanvasView _owner;
        private readonly NodeConfig _config;

        /// <summary>추가되는 <paramref name="config"/>를 기억해두는 생성자.</summary>
        public AddNodeCommand(FlowCanvasView owner, NodeConfig config)
        {
            _owner = owner;
            _config = config;
        }

        /// <inheritdoc />
        public string Description => $"노드 추가: {_config.Name}";

        /// <inheritdoc />
        public void Do()
        {
            _owner._nodeConfigs[_config.Id] = _config;
            _owner.RedrawActiveTab();
        }

        /// <inheritdoc />
        public void Undo()
        {
            _owner._nodeConfigs.Remove(_config.Id);
            _owner.RedrawActiveTab();
        }
    }

    /// <summary>
    /// Class명 : 와이어 연결 커맨드
    /// 역활 및 기능 : 출력 포트와 입력 포트를 잇는 Wire 하나를 추가하는 Undo/Redo 가능한 커맨드
    ///
    /// <see cref="OnCanvasMouseUp"/>에서 포트 드래그가 유효한 입력 포트 위에서 끝났을 때 실행됩니다.
    /// <see cref="Wire"/>가 값 동등성을 갖는 record라 Undo 시 <c>List.Remove</c>로 정확히 이 와이어
    /// 하나만 지울 수 있습니다(같은 두 포트 사이에 와이어를 두 번 만들 수 없다는 전제 — EC-02가 이미
    /// 같은 자기 자신 노드로의 연결만 막아뒀을 뿐 중복 와이어 자체는 막지 않지만, 그 경우에도
    /// List.Remove는 먼저 찾은 것 하나만 지우므로 Undo/Redo 짝은 항상 맞게 동작합니다).
    /// </summary>
    private sealed class AddWireCommand : IEditorCommand
    {
        private readonly FlowCanvasView _owner;
        private readonly Wire _wire;

        /// <summary>추가되는 <paramref name="wire"/>를 기억해두는 생성자.</summary>
        public AddWireCommand(FlowCanvasView owner, Wire wire)
        {
            _owner = owner;
            _wire = wire;
        }

        /// <inheritdoc />
        public string Description => "와이어 연결";

        /// <inheritdoc />
        public void Do()
        {
            _owner._wires.Add(_wire);
            _owner.RedrawActiveTab();
        }

        /// <inheritdoc />
        public void Undo()
        {
            _owner._wires.Remove(_wire);
            _owner.RedrawActiveTab();
        }
    }

    /// <summary>
    /// Class명 : 노드 속성 편집 커맨드
    /// 역활 및 기능 : NodePropertyDialog에서 "완료"로 확정한 NodeConfig 교체를 Undo/Redo 가능하게 만드는 커맨드
    ///
    /// <see cref="OpenPropertyDialog"/>가 다이얼로그를 닫을 때 실행됩니다. <paramref name="before"/>/
    /// <paramref name="after"/> 두 스냅샷을 통째로 기억해두는 방식(필드 단위 diff가 아님)이라 구현이
    /// 단순합니다 — <see cref="NodeConfig"/>는 불변 record라 두 스냅샷을 들고 있어도 이후 다른 편집이
    /// 이 값을 몰래 바꿀 수 없습니다.
    /// </summary>
    private sealed class EditNodePropertiesCommand : IEditorCommand
    {
        private readonly FlowCanvasView _owner;
        private readonly string _nodeId;
        private readonly NodeConfig _before;
        private readonly NodeConfig _after;

        /// <summary>편집 전(<paramref name="before"/>)/후(<paramref name="after"/>) 스냅샷을 기억해두는 생성자.</summary>
        public EditNodePropertiesCommand(FlowCanvasView owner, string nodeId, NodeConfig before, NodeConfig after)
        {
            _owner = owner;
            _nodeId = nodeId;
            _before = before;
            _after = after;
        }

        /// <inheritdoc />
        public string Description => $"노드 속성 편집: {_after.Name}";

        /// <inheritdoc />
        public void Do()
        {
            _owner._nodeConfigs[_nodeId] = _after;
            _owner.RedrawActiveTab();
        }

        /// <inheritdoc />
        public void Undo()
        {
            _owner._nodeConfigs[_nodeId] = _before;
            _owner.RedrawActiveTab();
        }
    }
}
