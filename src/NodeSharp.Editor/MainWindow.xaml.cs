using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Core;
using NodeSharp.Editor.Views;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 메인 창
/// 역활 및 기능 : NodeSharp.Editor의 최상위 WPF 창(ED-B0 시점에는 테마만 적용된 빈 창)
///
/// (ED-B0) 지금은 MainWindow.xaml의 DynamicResource 바인딩으로 테마 색상만 확인하는 빈 창입니다.
/// 헤더+메뉴+본문 레이아웃(ED-B1), Flow/구조설정 통합 뷰(ED-B2a) 등은 이후 Step에서 이 창 안에
/// 채워집니다.
/// (ED-B2b) Sequence Editor·Dashboard 진입점(헤더 "보기" 메뉴 + 좌측 네비게이션)을 클릭하면
/// 안내 메시지를 띄우는 두 핸들러가 추가됐습니다 — 실제 창은 Phase 10/11에서 만들어집니다.
/// (ED-B3) OS 기본 제목표시줄을 없애고(WindowStyle="None"+WindowChrome) 커스텀 타이틀바를 직접
/// 그렸습니다 — 최소화/최대화-복원/닫기 버튼 3개의 클릭 핸들러(<see cref="OnMinimizeClick"/>/
/// <see cref="OnMaximizeRestoreClick"/>/<see cref="OnCloseClick"/>)와, 최대화 시 작업표시줄을
/// 가리는 WindowChrome 특유의 문제를 방지하는 <see cref="OnWindowStateChanged"/>가 추가됐습니다.
/// (EC-04) "파일 → 저장" 메뉴와 Ctrl+S(Window.InputBindings/CommandBindings, ApplicationCommands.Save)
/// 가 공유하는 <see cref="OnSaveFlowClick"/>가 추가됐습니다 — 캔버스(<c>FlowCanvas</c>, x:Name)의
/// <c>SaveFlowAsync()</c>를 호출해 flows.json에 저장합니다.
/// (EC-06) 같은 패턴으로 "편집 → 복사"/Ctrl+C가 공유하는 <see cref="OnCopyNodeClick"/>과
/// "편집 → 붙여넣기"/Ctrl+V가 공유하는 <see cref="OnPasteNodeClick"/>이 추가됐습니다 — 각각
/// <c>FlowCanvas.CopySelectedNode()</c>/<c>FlowCanvas.PasteNode()</c>를 호출합니다.
/// (EC-07) "편집 → 실행 취소"/Ctrl+Z가 공유하는 <see cref="OnUndoClick"/>과 "편집 → 다시 실행"/
/// Ctrl+Y가 공유하는 <see cref="OnRedoClick"/>이 추가됐습니다 — 각각 <c>FlowCanvas.Undo()</c>/
/// <c>FlowCanvas.Redo()</c>를 호출합니다. 이 둘은 <see cref="OnUndoCanExecute"/>/
/// <see cref="OnRedoCanExecute"/>(CommandBinding.CanExecute)로 <c>FlowCanvas.CanUndo</c>/
/// <c>CanRedo</c>를 확인해, 되돌리거나 다시 실행할 것이 없으면 메뉴가 자동으로 비활성화됩니다.
/// (EC-10) 같은 패턴으로 "편집 → 그룹으로 묶기"/Ctrl+G가 공유하는 <see cref="OnGroupNodesClick"/>과
/// "편집 → 그룹 해제"/Ctrl+Shift+G가 공유하는 <see cref="OnUngroupNodesClick"/>이 추가됐습니다 —
/// 각각 <c>FlowCanvas.GroupSelectedNodes()</c>/<c>FlowCanvas.UngroupSelectedGroup()</c>를
/// 호출합니다. WPF ApplicationCommands에는 대응하는 표준 명령이 없어 <see cref="EditorCommands"/>에
/// 직접 선언한 <see cref="RoutedCommand"/> 2개(GroupNodes/UngroupNodes)를 대신 씁니다.
/// (EC-11) 우측 패널이 TabControl("구조 설정"/"Information")로 바뀌면서, 생성자에서
/// <c>FlowCanvas.SelectionChanged</c>를 <see cref="OnCanvasSelectionChanged"/>에 구독해 캔버스에서
/// 선택이 바뀔 때마다 <c>InformationPanel.Update(...)</c>를 호출하도록 연결했습니다.
/// (EC-12) 세 번째 탭 "Explorer"가 추가되면서, 같은 패턴으로 생성자에서
/// <c>ExplorerPanel.QueryChanged</c>/<c>ResultActivated</c>를 각각 <see cref="OnExplorerQueryChanged"/>/
/// <see cref="OnExplorerResultActivated"/>에 구독했습니다 — 검색어가 바뀔 때마다
/// <c>FlowCanvas.SearchNodes(...)</c>를 호출해 결과를 <c>ExplorerPanel.ShowResults(...)</c>로
/// 넘기고, 결과를 클릭하면 <c>FlowCanvas.NavigateToNode(...)</c>로 해당 Flow 탭 전환 + 노드
/// 하이라이트를 트리거합니다. "편집 → 찾기"/Ctrl+F(<c>ApplicationCommands.Find</c>)가 공유하는
/// <see cref="OnFindClick"/>은 <c>SidebarTabControl.SelectedItem</c>을 <c>ExplorerTab</c>으로 바꾸고
/// <c>ExplorerPanel.FocusSearchBox()</c>를 호출해 탭 전환과 동시에 검색창에 포커스를 줍니다.
/// (LK-02b) 네 번째 탭 "Debug"(<see cref="Views.DebugSidebarView"/>)와 타이틀바 연결 상태 배지
/// (<c>ConnectionStatusText</c>)가 추가되면서, <see cref="OnWindowLoaded"/>(<see cref="Window.Loaded"/>)가
/// <see cref="EditorMonitorClient"/>(Core, Runner의 "/hubs/monitor" SignalR Hub 클라이언트)를 만들어
/// 5가지 이벤트(<see cref="NodeStatusEvent"/>/<see cref="FlowActivityEvent"/>/<see cref="DebugMessageEvent"/>/
/// <see cref="NodeErrorEvent"/>/<see cref="TagValueUpdatedEvent"/>)와 연결 상태 변화를 각각
/// <c>FlowCanvas.ApplyNodeStatus</c>/<c>FlowCanvas.PulseWire</c>/<c>DebugPanel.AppendDebugMessage</c>/
/// <c>DebugPanel.AppendNodeError</c>/<c>FlowCanvas.ApplyTagValueUpdate</c>(ED-D11b, 5번째)/
/// <see cref="UpdateConnectionBadge"/>에 연결한 뒤 <c>StartAsync()</c>합니다. SignalR 콜백은 SignalR
/// 자체 스레드에서 오므로(WPF UI 스레드가 아님) 전부 <see cref="Dispatcher.Invoke(System.Action)"/>로
/// 감쌉니다 — 감싸지 않으면 UI 요소를 다른 스레드에서 건드리는 WPF 규칙 위반으로 예외가 납니다.
/// <see cref="OnWindowClosed"/>(<see cref="Window.Closed"/>)가 <c>DisposeAsync()</c>로 연결을 정리합니다.
/// (LK-02b 후속, ★ 사용자 요청 — "Node-RED처럼 배포 버튼 하나로 살아나는 경험을 원함") "파일" 메뉴에
/// "Runner 실행(배포)"(<see cref="OnRunnerDeployClick"/>)/"Runner 중지"(<see cref="OnRunnerStopClick"/>)
/// 2개 항목이 추가됐습니다 — <see cref="RunnerProcessManager"/>(신규, Core 폴더)로 Runner 실행 파일을
/// 자식 프로세스로 띄우거나 정지합니다. Editor와 Runner를 하나의 프로세스로 합치는 재설계는 하지
/// 않았습니다(<see cref="RunnerProcessManager"/> 클래스 remarks 참고 — 헤드리스 배포 요구사항 유지) —
/// 대신 이 메뉴가 "버튼 하나로 실행"이라는 체감만 제공합니다.
/// (★ 버그 수정, 2026-08-14 — 사용자가 "프로그램 종료 시 예외 발생"으로 보고) <see cref="OnWindowLoaded"/>가
/// 구독하는 5개 <c>_monitorClient</c> 이벤트가 <c>Dispatcher.Invoke</c>를 직접 호출하는 대신 신규
/// <see cref="SafeDispatcherInvoke"/>를 거치도록 통일했습니다 — 창이 닫히는 시점에 <c>HubConnection</c>
/// 정리(<c>Closed</c> 이벤트)가 겹치면 이 창의 <see cref="Dispatcher"/>가 이미 종료 중일 수 있어 생기던
/// 예외를 막습니다(자세한 근본 원인·수정 내용은 <see cref="SafeDispatcherInvoke"/> 자체 문서 참고).
/// (LK-03) runner.token 기반 인증이 <see cref="EditorMonitorClient"/>에 추가되면서, <see cref="OnWindowLoaded"/>의
/// 순서가 바뀌었습니다 — 이제 <c>_monitorClient.StartAsync()</c>보다 <b>먼저</b>
/// <c>_runnerProcessManager.LoadPathAsync</c>(Runner 실행 파일 경로, 같은 PC 판단 근거)와
/// <see cref="RunnerTokenCache.ResolveAsync"/>(같은 PC면 Runner 폴더의 <c>runner.token</c>을,
/// 아니면 로컬 캐시를 읽음)를 호출해 <c>_monitorClient.SetToken(...)</c>까지 끝낸 뒤 연결을
/// 시도합니다 — 토큰 없이 먼저 연결하면 <c>TokenAuthMiddleware</c>가 401로 거부하기 때문입니다.
/// "파일" 메뉴에 새로 추가된 "토큰 재발급"(<see cref="OnReissueTokenClick"/>)/"Runner 토큰 입력"
/// (<see cref="OnEnterTokenClick"/>, <see cref="Views.TokenInputDialog"/> 모달 사용) 2개 항목과,
/// 다른 연결에서 재발급이 트리거됐을 때 이 연결도 스스로 끊도록 하는 <see cref="OnTokenInvalidatedByServer"/>
/// (<see cref="EditorMonitorClient.TokenInvalidatedByServer"/> 구독)가 함께 추가됐습니다.
/// (ED-D14, ★ 완료 기준 — "30초 경과 시 스냅샷이 생성되고, 비정상 종료 후 재기동 시 복구
/// 다이얼로그가 표시되는지 확인") 생성자가 신규 <see cref="AutosaveService"/>(Core)를 만들어
/// <c>CheckAndPromptRecovery()</c>(비정상 종료 흔적 확인 + 필요 시 복구 모달)를 먼저 호출하고
/// <c>Start()</c>로 30초 주기 자동저장을 시작합니다 — <see cref="OnWindowClosed"/>가 창을 정상적으로
/// 닫을 때 <c>Dispose()</c>·<c>ClearOnCleanExit()</c>로 정리합니다. 설계 근거(왜 02번 문서 8번 탭
/// 카드17 원안의 통합 <c>EditorSessionState</c> 대신 각 뷰의 기존 더티 판정을 재사용했는지 등)는
/// <see cref="AutosaveService"/> 클래스 자체 주석 참고.
/// </summary>
public partial class MainWindow : Window
{
    // (LK-02b) Runner SignalR Hub 클라이언트 — 생성만 여기서 하고 실제 연결 시도는 OnWindowLoaded에서
    // 한다(창이 완전히 그려진 뒤에 시작해야 FlowCanvas/DebugPanel의 x:Name 필드가 확실히 준비됨).
    private readonly EditorMonitorClient _monitorClient = new();

    // (LK-02b 후속, 사용자 요청) "Runner 실행(배포)"/"Runner 중지" 메뉴가 쓰는 프로세스 관리자.
    private readonly RunnerProcessManager _runnerProcessManager = new();

    // (ED-D14) 30초 주기 자동저장·크래시 복구 서비스 — 생성자에서 FlowCanvas/StructureTab이 만들어진
    // 뒤에야 구성 가능해 필드 초기화식이 아니라 생성자 본문에서 대입한다(아래 생성자 참고).
    private readonly AutosaveService _autosaveService;

    /// <summary>
    /// XAML에서 정의한 컨트롤들을 초기화합니다(WPF 표준 패턴). (EC-11) <c>FlowCanvas</c>는
    /// <c>InitializeComponent</c> 직후 이미 생성돼 있으므로(x:Name 필드), <see cref="Window.Loaded"/>를
    /// 기다리지 않고 바로 <c>SelectionChanged</c>를 구독합니다. (EC-12) <c>ExplorerPanel</c>의
    /// <c>QueryChanged</c>/<c>ResultActivated</c>도 같은 방식으로 함께 구독합니다. (ED-D12)
    /// <c>StructureTab.TagNodeSelected</c>도 동일하게 여기서 구독해 <c>FlowCanvas.HighlightNodesByTagRef</c>로
    /// 연결합니다. (ED-D13) <c>StructureTab.History</c>에 <c>FlowCanvas.History</c>를 그대로 대입해
    /// 캔버스·구조 트리 커맨드가 같은 Undo/Redo 스택을 공유하게 합니다(이 창의 기존
    /// <c>OnUndoClick</c>/<c>OnRedoClick</c>/<c>OnUndoCanExecute</c>/<c>OnRedoCanExecute</c>는 계속
    /// <c>FlowCanvas.Undo()</c>/<c>Redo()</c>/<c>CanUndo</c>/<c>CanRedo</c>만 호출하므로 손댈 필요가
    /// 없습니다 — 이제 그 프로퍼티들이 구조 트리 커맨드까지 포함한 같은 스택을 가리킵니다). (ED-D14)
    /// <see cref="_autosaveService"/>를 만든 직후 <c>CheckAndPromptRecovery()</c>를 먼저 호출합니다
    /// — <c>FlowCanvas</c>/<c>StructureTab</c>의 <see cref="Window.Loaded"/>는 이 생성자가 끝난
    /// 뒤에야 발생하므로, 복구 다이얼로그(있다면)와 <c>PendingAutosaveOverrideJson</c> 설정이 항상
    /// 두 뷰의 자동 로드보다 먼저 끝나 있음이 보장됩니다(<c>AutosaveService</c> 클래스 자체 주석 참고).
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        FlowCanvas.SelectionChanged += OnCanvasSelectionChanged;
        ExplorerPanel.QueryChanged += OnExplorerQueryChanged;
        ExplorerPanel.ResultActivated += OnExplorerResultActivated;
        // (LK-02b 후속) FlowCanvas.InjectTriggerRequested는 SignalR을 몰라도 되므로, 이 창이 대신
        // EditorMonitorClient.TriggerInjectAsync를 호출한다(OnInjectTriggerRequested 참고).
        FlowCanvas.InjectTriggerRequested += OnInjectTriggerRequested;
        // (ED-D12) 구조 트리에서 태그를 선택하면(또는 선택 해제하면) 캔버스 쪽에 그대로 반영 —
        // SignalR과 무관한 순수 UI 이벤트라 위 3개와 마찬가지로 Window.Loaded를 기다리지 않는다.
        StructureTab.TagNodeSelected += tagId => FlowCanvas.HighlightNodesByTagRef(tagId);
        // (ED-D13) 구조 트리 커맨드(추가/삭제/이름변경)가 캔버스와 같은 CommandHistory 스택에 쌓이도록
        // 연결 — FlowCanvas.History는 이 시점에 이미 생성돼 있다(EC-11과 동일한 이유로 Loaded를
        // 기다릴 필요 없음).
        StructureTab.History = FlowCanvas.History;
        // (PD-01d, ★ 추가) 시뮬레이터 탭이 SimulationMode=true PlcNode를 찾으려면 구조 트리(루트
        // 컬렉션)가 필요 — StructureTab.Devices는 이 시점에 이미 생성된 ObservableCollection<T>
        // 인스턴스(내용은 StructureTab의 Window.Loaded에서 비동기로 채워짐)라, 참조만 지금 넘겨주면
        // 충분하다(EC-11/ED-D12와 동일한 이유로 Loaded를 기다릴 필요 없음 — SimulatorPanelView는
        // 탭이 실제로 선택될 때 그 시점의 최신 내용을 다시 훑는다, "새로고침" 참고).
        SimulatorPanel.SetDeviceTree(StructureTab.Devices);
        // (PD-01e, ★ 추가) 시뮬레이터 탭이 레지스터 값을 편집할 때 Runner에 원격 기입(SignalR)하려면
        // 이 창이 이미 만든 _monitorClient가 필요하다 — 위 SetDeviceTree와 동일한 이유로 지금 참조만
        // 넘겨주면 충분하다(_monitorClient.StartAsync()는 아직 호출 전이라 연결 자체는 나중에
        // OnWindowLoaded에서 이루어지지만, SimulatorPanelView는 호출 시점에 IsConnected만 확인하므로
        // 문제 없다).
        SimulatorPanel.SetMonitorClient(_monitorClient);

        // (ED-D14) 자동저장·크래시 복구 — CheckAndPromptRecovery()는 두 뷰의 Loaded가 발생하기 전에
        // 반드시 끝나야 하므로(위 클래스 문서 참고) Start()보다 먼저, 그리고 InitializeComponent()
        // 직후인 지금 이 자리에서 동기적으로 호출한다.
        _autosaveService = new AutosaveService(FlowCanvas, StructureTab, Dispatcher);
        _autosaveService.CheckAndPromptRecovery();
        _autosaveService.Start();
    }

    /// <summary>
    /// (LK-02b) 창이 완전히 로드된 뒤 <see cref="_monitorClient"/>의 이벤트를 캔버스/Debug 패널/연결
    /// 배지에 연결하고 <c>StartAsync()</c>를 호출합니다. Runner가 아직 실행 중이 아니어도
    /// (<see cref="EditorMonitorClient"/> 클래스 remarks의 "실패 격리" 항목) 예외 없이 조용히
    /// "연결 안됨" 상태로 남고, Editor는 오프라인 편집을 계속할 수 있습니다.
    /// (LK-03) <c>StartAsync()</c>를 호출하기 <b>전</b>에 <c>_runnerProcessManager.LoadPathAsync</c>와
    /// <see cref="RunnerTokenCache.ResolveAsync"/>로 인증 토큰을 먼저 알아내 <c>_monitorClient.SetToken</c>에
    /// 반영합니다 — 순서가 바뀐 이유는 클래스 remarks 참고. 토큰을 전혀 못 찾아도(최초 원격 PC
    /// 연결 등) 예외 없이 그대로 연결을 시도해 401로 조용히 거부되고(<see cref="EditorMonitorClient"/>의
    /// "실패 격리"), 사용자가 "Runner 토큰 입력" 메뉴로 직접 입력하면 됩니다.
    /// </summary>
    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _monitorClient.ConnectionStateChanged += connected => SafeDispatcherInvoke(() => UpdateConnectionBadge(connected));
        _monitorClient.NodeStatusReceived += evt => SafeDispatcherInvoke(() => FlowCanvas.ApplyNodeStatus(evt));
        _monitorClient.FlowActivityReceived += evt => SafeDispatcherInvoke(() => FlowCanvas.PulseWire(evt));
        _monitorClient.DebugMessageReceived += evt => SafeDispatcherInvoke(() => DebugPanel.AppendDebugMessage(evt));
        // (LK-04) 에러 항목은 즉시 추가하고, Msg Trace는 별도 SignalR 왕복(GetMsgTraceAsync)이 필요해
        // 뒤이어 비동기로 조회한 뒤 도착하는 대로 이어 붙인다 — OnNodeErrorReceivedAsync 참고.
        _monitorClient.NodeErrorReceived += evt =>
        {
            SafeDispatcherInvoke(() => DebugPanel.AppendNodeError(evt));
            _ = OnNodeErrorReceivedAsync(evt);
        };
        // (ED-D11b) 5번째 이벤트 — 스로틀(초당 5회)은 FlowCanvasView.ApplyTagValueUpdate 내부가 담당.
        _monitorClient.TagValueReceived += evt => SafeDispatcherInvoke(() => FlowCanvas.ApplyTagValueUpdate(evt));
        // (LK-03) Runner가 다른 연결에 재발급을 알리면(이 창이 재발급을 트리거한 당사자가 아니면)
        // 스스로 끊고 사용자에게 새 토큰 재입력을 안내한다.
        _monitorClient.TokenInvalidatedByServer += () => SafeDispatcherInvoke(OnTokenInvalidatedByServer);

        // (LK-02b 후속) 이전에 사용자가 선택해둔 Runner 실행 파일 경로가 있으면 불러온다 — "Runner
        // 실행(배포)" 메뉴를 눌렀을 때 매번 다시 물어보지 않기 위함. (LK-03) 이 경로가 같은 PC
        // 판단 근거이기도 해 아래 토큰 해석보다 먼저 불러와야 한다.
        await _runnerProcessManager.LoadPathAsync(FlowCanvas.DataDirectory);

        // (LK-03) 같은 PC면 Runner 폴더의 runner.token을, 아니면 이전에 저장해둔 캐시 값을 읽어
        // 인증 헤더를 채운다 — 둘 다 없으면 token은 null로 남아 토큰 없이 연결을 시도한다(그대로
        // 401로 거부됨, "Runner 토큰 입력" 메뉴로 사용자가 직접 입력 가능).
        var token = await RunnerTokenCache.ResolveAsync(_runnerProcessManager.RunnerExecutablePath, FlowCanvas.DataDirectory);
        _monitorClient.SetToken(token);

        await _monitorClient.StartAsync();
    }

    /// <summary>
    /// (LK-03) <see cref="EditorMonitorClient.TokenInvalidatedByServer"/> 수신 시 호출됩니다 — 다른
    /// 연결(다른 Editor 인스턴스 등)에서 토큰이 재발급됐다는 뜻이므로, 이 연결을 정리하고 사용자에게
    /// "Runner 토큰 입력" 메뉴로 새 토큰을 입력하라고 안내합니다.
    /// </summary>
    private async void OnTokenInvalidatedByServer()
    {
        await _monitorClient.StopAsync();
        MessageBox.Show(
            "다른 곳에서 Runner 토큰이 재발급되어 이 연결이 더 이상 유효하지 않습니다.\n\"파일 → Runner 토큰 입력\" 메뉴로 새 토큰을 입력한 뒤 다시 연결해 주세요.",
            "토큰 재인증 필요",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// (LK-04) <see cref="EditorMonitorClient.NodeErrorReceived"/> 수신 직후 호출됩니다 —
    /// <paramref name="evt"/>.<c>MsgId</c>로 <c>GetMsgTraceAsync</c>를 호출해 이 메시지가 거쳐온 경로를
    /// 받아와 <see cref="DebugPanel"/>에 이어 붙입니다("Msg Trace로 에러 발생 노드와 해당 시점 Msg
    /// 내용까지 역추적", 03번 Step맵 LK-04 완료 기준). 연결이 그 사이 끊겼거나(<see cref="EditorMonitorClient.IsConnected"/>가
    /// <c>false</c>) 조회 자체가 실패해도(예: Runner가 재시작돼 이미 메모리에서 지워짐) 조용히
    /// 무시합니다 — Trace는 원인 분석을 돕는 보조 정보일 뿐, 이미 <see cref="DebugSidebarView.AppendNodeError"/>가
    /// 표시한 예외 메시지·Msg 스냅샷만으로도 "무엇이 잘못됐는지"는 이미 확인 가능하므로 Trace 조회
    /// 실패가 사용자에게 별도 오류로 보일 필요는 없습니다(<c>EditorMonitorClient</c> "실패 격리"
    /// 원칙과 동일한 정신).
    /// </summary>
    private async Task OnNodeErrorReceivedAsync(NodeErrorEvent evt)
    {
        if (!_monitorClient.IsConnected)
        {
            return;
        }

        try
        {
            var trace = await _monitorClient.GetMsgTraceAsync(evt.MsgId);
            if (trace is not null)
            {
                SafeDispatcherInvoke(() => DebugPanel.AppendMsgTrace(evt.MsgId, trace));
            }
        }
        catch (Exception)
        {
            // 위 XML 문서 참고 — Trace는 보조 정보라 조회 실패를 사용자에게 별도로 알리지 않는다.
        }
    }

    /// <summary>
    /// (★ 버그 수정, 2026-08-14 — 사용자가 "프로그램 종료 시 OnWindowLoaded의 익명 메서드에서 예외
    /// 발생"으로 보고) <see cref="Dispatcher.Invoke(System.Action)"/>를 이 메서드로 감싸 대신
    /// 호출합니다. <b>근본 원인</b>: <see cref="OnWindowClosed"/>가 <c>_monitorClient.DisposeAsync()</c>를
    /// 호출하면 내부 <c>HubConnection</c>이 정리되며 <c>Closed</c> 이벤트가 발생하는데(SignalR 클라이언트의
    /// 잘 알려진 동작 — 의도적인 정상 종료에도 <c>Closed</c>가 발생함), <c>EditorMonitorClient</c> 생성자가
    /// 이 이벤트를 <c>ConnectionStateChanged?.Invoke(false)</c>로 재발행하도록 이미 연결해뒀습니다(<see cref="Core.EditorMonitorClient"/>
    /// 참고) — 이 재발행이 <see cref="OnWindowLoaded"/>가 구독해둔 람다(<c>Dispatcher.Invoke(...)</c>)를
    /// 트리거하는데, 창이 닫히는 시점(마지막 창이 닫히면 기본 <c>ShutdownMode</c>가 곧바로 Application
    /// 종료를 시작함)과 겹치면 이 창의 <see cref="Dispatcher"/>가 이미 종료를 시작했거나 끝난 상태일 수
    /// 있어 <c>Dispatcher.Invoke</c> 자체가 예외를 던집니다. <b>수정</b>: <see cref="Dispatcher.HasShutdownStarted"/>/
    /// <see cref="Dispatcher.HasShutdownFinished"/>를 먼저 확인해 종료 중이면 아무 것도 하지 않고
    /// 조용히 반환하고(창이 닫히는 중엔 배지·캔버스를 갱신해도 더 이상 아무도 보지 않으므로 안전),
    /// 그 사이의 아주 좁은 경합(체크 직후·Invoke 직전에 종료가 시작되는 경우)까지 대비해
    /// <c>Dispatcher.Invoke</c> 호출 자체도 <c>try/catch</c>로 감쌉니다 — <see cref="OnWindowClosed"/>가
    /// 정리 실패를 조용히 삼키는 것과 동일한 "종료 중 실패는 새삼 알릴 문제가 아니다" 원칙입니다. 5개
    /// 이벤트(<c>ConnectionStateChanged</c>/<c>NodeStatusReceived</c>/<c>FlowActivityReceived</c>/
    /// <c>DebugMessageReceived</c>/<c>NodeErrorReceived</c>) 구독이 전부 이 메서드를 거치도록 통일해,
    /// 지금 재현된 <c>Closed</c> 경로뿐 아니라 창이 닫히는 순간 Runner가 다른 이벤트를 보내는 드문
    /// 경합에도 동일하게 방어됩니다.
    /// </summary>
    private void SafeDispatcherInvoke(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            Dispatcher.Invoke(action);
        }
        catch (Exception)
        {
            // 위 요약 참고 — 체크 직후 종료가 시작되는 아주 좁은 경합까지 대비한 안전망, 조용히 무시.
        }
    }

    /// <summary>
    /// (LK-02b) 타이틀바 <c>ConnectionStatusText</c>를 연결 상태에 맞게 바꿉니다 — Node-RED 5.0의
    /// 연결 상태 점(초록/회색)과 같은 개념을 텍스트 배지 하나로 단순화했습니다.
    /// </summary>
    private void UpdateConnectionBadge(bool connected)
    {
        ConnectionStatusText.Text = connected ? "🟢 Runner 연결됨" : "⚪ Runner 연결 안됨";
        ConnectionStatusText.Foreground = (Brush)FindResource(connected ? "GreenBrush" : "SecondaryTextBrush");
    }

    /// <summary>
    /// (LK-02b) 창이 닫힐 때 <see cref="_monitorClient"/>를 정리합니다(<see cref="EditorMonitorClient"/>
    /// 클래스 remarks의 "구독 해제" 항목). <see cref="Window.Closed"/> 핸들러는 <c>async void</c>만
    /// 가능해(반환값을 기다려줄 호출자가 없음) 실패해도 프로세스 종료를 막지 않도록 예외를 삼킵니다
    /// (창을 닫는 도중의 정리 실패가 사용자에게 새삼 알릴 만한 문제는 아니라고 판단). (ED-D14) 이어서
    /// <see cref="_autosaveService"/>를 멈추고 <c>.autosave</c> 스냅샷을 지웁니다 — 지금 이 핸들러
    /// 자체가 "정상 종료" 경로이므로(크래시·강제 종료면 애초에 이 핸들러가 실행되지 않음), 다음 시작
    /// 때 <c>AutosaveService.CheckAndPromptRecovery</c>가 복구를 제안하지 않게 됩니다(카드17 표
    /// "정상 종료 시" 항목, <c>AutosaveService</c> 클래스 자체 주석 참고).
    /// </summary>
    private async void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            await _monitorClient.DisposeAsync();
        }
        catch
        {
            // (위 요약 참고) 창을 닫는 중의 정리 실패는 조용히 무시한다.
        }

        try
        {
            _autosaveService.Dispose();
            _autosaveService.ClearOnCleanExit();
        }
        catch
        {
            // 위와 동일한 이유 — 창을 닫는 중의 정리 실패는 조용히 무시한다.
        }
    }

    /// <summary>
    /// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <c>FlowCanvas.InjectTriggerRequested</c>가
    /// 발생하면(캔버스의 ▶ 버튼 클릭) <c>EditorMonitorClient.TriggerInjectAsync</c>로 위임합니다.
    /// 아직 Runner에 연결돼 있지 않으면(<c>_monitorClient.IsConnected</c>가 <c>false</c>) 호출 자체를
    /// 시도하지 않고 안내 메시지만 띄웁니다(연결 안 된 상태에서 <c>InvokeAsync</c>를 호출하면 예외가
    /// 나므로, 사용자에게 "왜 안 되는지"를 명확히 알려주는 편이 더 낫다고 판단). 연결은 돼 있지만
    /// 호출 자체가 실패하면(드묾 — 예: 호출 도중 연결이 막 끊긴 경합) 그 예외 메시지를 그대로 보여줍니다.
    /// </summary>
    private async void OnInjectTriggerRequested(string nodeId)
    {
        if (!_monitorClient.IsConnected)
        {
            MessageBox.Show(
                "Runner에 연결되어 있지 않아 트리거할 수 없습니다. Runner를 실행한 뒤 다시 시도해 주세요.",
                "Inject 트리거 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _monitorClient.TriggerInjectAsync(nodeId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Inject 트리거 중 오류가 발생했습니다.\n{ex.Message}",
                "Inject 트리거 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// (EC-11) <c>FlowCanvas.SelectionChanged</c>가 발생할 때마다(선택/해제/다중 선택/탭 전환·Undo·
    /// Redo로 인한 재렌더링) <c>InformationPanel.Update(...)</c>를 그대로 위임 호출합니다 — 정확히
    /// 노드 하나가 선택돼 있으면 그 정보를, 아니면 안내 문구로 되돌립니다.
    /// </summary>
    private void OnCanvasSelectionChanged(NodeConfig? config, INodeTypeDescriptor? descriptor) =>
        InformationPanel.Update(config, descriptor);

    /// <summary>
    /// (EC-12) <c>ExplorerPanel.QueryChanged</c>(검색창 텍스트가 바뀔 때마다)가 발생하면
    /// <c>FlowCanvas.SearchNodes(query)</c>로 모든 Flow 탭에 걸쳐 노드를 찾고, 그 결과를
    /// <c>ExplorerPanel.ShowResults(...)</c>로 그대로 넘겨 화면을 갱신합니다.
    /// </summary>
    private void OnExplorerQueryChanged(string query) =>
        ExplorerPanel.ShowResults(FlowCanvas.SearchNodes(query));

    /// <summary>
    /// (EC-12) <c>ExplorerPanel.ResultActivated</c>(검색 결과 하나를 클릭하면 (FlowId, NodeId))가
    /// 발생하면 <c>FlowCanvas.NavigateToNode(flowId, nodeId)</c>를 그대로 위임 호출해, 해당 Flow
    /// 탭으로 전환하고 노드를 선택 상태로 만들어 하이라이트합니다(완료 기준).
    /// </summary>
    private void OnExplorerResultActivated(string flowId, string nodeId) =>
        FlowCanvas.NavigateToNode(flowId, nodeId);

    /// <summary>
    /// (EC-12) "편집 → 찾기" 메뉴 Click과 Ctrl+F(<c>ApplicationCommands.Find</c>의
    /// <c>CommandBinding.Executed</c>)가 공유하는 핸들러입니다. <c>SidebarTabControl.SelectedItem</c>을
    /// <c>ExplorerTab</c>으로 바꿔 Explorer 탭을 화면에 띄우고, <c>ExplorerPanel.FocusSearchBox()</c>를
    /// 호출해 검색창에 곧바로 포커스를 줍니다(탭을 직접 클릭할 필요 없이 Ctrl+F 한 번으로 바로
    /// 검색어를 입력할 수 있게 하기 위함).
    /// </summary>
    private void OnFindClick(object sender, RoutedEventArgs e)
    {
        SidebarTabControl.SelectedItem = ExplorerTab;
        ExplorerPanel.FocusSearchBox();
    }

    /// <summary>
    /// (ED-B2b) 헤더 "보기 → Sequence Editor" 메뉴와 좌측 네비게이션 "Sequence" 항목이 공유하는
    /// 클릭 핸들러입니다. 실제 Sequence Editor 창(11번 탭 카드6, 캔버스와 별개의 독립 Window)은
    /// Phase 10에서 만들어지므로, 지금은 안내 메시지만 띄웁니다. <paramref name="e"/>는
    /// <see cref="RoutedEventArgs"/>를 받는 메서드가 <c>MouseButtonEventHandler</c>(
    /// <see cref="System.Windows.Input.MouseButtonEventArgs"/> 파생)에도 그대로 연결될 수 있게
    /// 하는 C# 델리게이트 반공변성을 이용해, 메뉴 Click과 네비게이션 MouseLeftButtonUp 두 이벤트를
    /// 같은 메서드 하나로 처리합니다.
    /// </summary>
    private void OnSequenceEditorEntryClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Sequence Editor는 Phase 10(Sequence)에서 별도 창으로 제공될 예정입니다.",
            "Sequence Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// (ED-B2b) 헤더 "보기 → Dashboard" 메뉴와 좌측 네비게이션 "Dashboard" 항목이 공유하는 클릭
    /// 핸들러입니다. 실제 Dashboard 화면(9번 탭, 웹+WPF 듀얼 렌더링)은 Phase 11에서 만들어지므로,
    /// 지금은 안내 메시지만 띄웁니다.
    /// </summary>
    private void OnDashboardEntryClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Dashboard는 Phase 11(Dashboard)에서 별도 창으로 제공될 예정입니다.",
            "Dashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>(ED-B3) 커스텀 타이틀바의 최소화 버튼 — 창을 최소화합니다.</summary>
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>(ED-B3) 커스텀 타이틀바의 최대화/복원 버튼 — 현재 상태의 반대로 전환합니다.</summary>
    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>(ED-B3) 커스텀 타이틀바의 닫기 버튼 — 창을 닫습니다(표준 <see cref="Window.Close"/>).</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// (EC-04) "파일 → 저장" 메뉴 Click과 Ctrl+S(CommandBinding.Executed, <see cref="ExecutedRoutedEventArgs"/>도
    /// <see cref="RoutedEventArgs"/> 파생이라 같은 델리게이트 반공변성 기법으로 한 메서드가 두 이벤트를
    /// 모두 처리)이 공유하는 저장 핸들러입니다. <c>FlowCanvas.SaveFlowAsync()</c>를 호출해 지금
    /// 캔버스 상태를 flows.json에 원자적으로 저장하고, 성공/실패를 안내 메시지로 알립니다.
    /// (ED-D03) <c>StructureTab.SaveDeviceTreeAsync()</c>를 이어서 호출해 구조 설정 트리도 device.json에
    /// 함께 원자적으로 저장합니다 — 이 앱은 "저장" 동작 하나(Ctrl+S)로 flows.json/device.json을 함께
    /// 다루도록 통합했습니다(각 트리마다 별도 저장 버튼을 두지 않음, StructureView 클래스 remarks 참고).
    /// 두 파일은 서로 독립적인 저장이므로 각각 별도 try/catch로 감싸 한쪽이 실패해도 다른 쪽 저장은
    /// 계속 시도합니다.
    /// (ED-D05) 실제 저장(=LK-01 자동 재배포 트리거) 직전에 <see cref="Views.FlowCanvasView.FindBrokenTagRefs"/>로
    /// TagRef 무결성을 먼저 검사합니다 — 위반이 있으면 목록을 보여주고 "그래도 저장하시겠습니까?"를
    /// 물어, "아니오"를 선택하면 저장 자체를 하지 않고 메서드를 끝냅니다(완료 기준 "배포를 막거나
    /// 경고" — 기본은 막되, 사용자가 명시적으로 승인하면 진행할 수 있게 함).
    /// </summary>
    private async void OnSaveFlowClick(object sender, RoutedEventArgs e)
    {
        var brokenTagRefs = FlowCanvas.FindBrokenTagRefs();
        if (brokenTagRefs.Count > 0)
        {
            var list = string.Join("\n", brokenTagRefs.Select(b =>
                $"- {(string.IsNullOrWhiteSpace(b.NodeName) ? b.NodeId : b.NodeName)} ({b.FieldKey}: 존재하지 않는 태그 \"{b.MissingTagId}\")"));
            var choice = MessageBox.Show(
                $"다음 노드가 더 이상 존재하지 않는 태그를 참조하고 있습니다(구조 설정에서 삭제되었을 수 있습니다):\n\n{list}\n\n" +
                "그래도 저장하시겠습니까?",
                "TagRef 연동 끊김",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            await FlowCanvas.SaveFlowAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"flows.json 저장 중 오류가 발생했습니다.\n{ex.Message}",
                "저장 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        try
        {
            await StructureTab.SaveDeviceTreeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"device.json 저장 중 오류가 발생했습니다.\n{ex.Message}",
                "저장 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// (LK-02b 후속, ★ 사용자 요청 — "Node-RED의 Deploy 버튼 같은 경험") "파일 → Runner 실행(배포)"
    /// 클릭 핸들러입니다. 이미 Runner에 연결돼 있으면(<c>_monitorClient.IsConnected</c>) 새로 띄울
    /// 필요가 없으므로 <see cref="OnSaveFlowClick"/>만 재사용해 저장 → LK-01 자동 재배포를 트리거합니다.
    /// 연결돼 있지 않으면 <see cref="RunnerProcessManager.RunnerExecutablePath"/>가 비어 있거나 그
    /// 파일이 더 이상 없을 때만 <see cref="OpenFileDialog"/>로 Runner 실행 파일(.exe/.dll)을 한 번
    /// 물어보고(이후 <see cref="RunnerProcessManager.SavePathAsync"/>로 기억해 다음부터는 다시 묻지
    /// 않음), <see cref="RunnerProcessManager.Start"/>로 자식 프로세스를 띄웁니다. <c>HubConnection</c>의
    /// <c>WithAutomaticReconnect()</c>는 "한 번 연결된 뒤 끊긴 경우"만 자동 재시도하고 최초
    /// <c>StartAsync</c> 실패는 재시도하지 않으므로(<see cref="EditorMonitorClient"/> 클래스 remarks
    /// 참고), Runner의 Kestrel이 포트를 열 때까지 짧은 간격으로 직접 재시도합니다. 연결에 성공하면
    /// 즉시 저장까지 이어서 실행해(첫 배포) "누르면 바로 살아난다"는 체감을 완성합니다.
    /// </summary>
    private async void OnRunnerDeployClick(object sender, RoutedEventArgs e)
    {
        if (_monitorClient.IsConnected)
        {
            OnSaveFlowClick(sender, e);
            return;
        }

        if (string.IsNullOrWhiteSpace(_runnerProcessManager.RunnerExecutablePath) ||
            !File.Exists(_runnerProcessManager.RunnerExecutablePath))
        {
            var dialog = new OpenFileDialog
            {
                Title = "NodeSharp.Runner 실행 파일 선택",
                Filter = "Runner 실행 파일 (*.exe;*.dll)|*.exe;*.dll|모든 파일 (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _runnerProcessManager.RunnerExecutablePath = dialog.FileName;
            await _runnerProcessManager.SavePathAsync(FlowCanvas.DataDirectory);
        }

        try
        {
            _runnerProcessManager.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Runner 실행 중 오류가 발생했습니다.\n{ex.Message}",
                "Runner 실행 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        for (var attempt = 0; attempt < 10 && !_monitorClient.IsConnected; attempt++)
        {
            await Task.Delay(500);
            await _monitorClient.StartAsync();
        }

        if (_monitorClient.IsConnected)
        {
            OnSaveFlowClick(sender, e);
        }
        else
        {
            MessageBox.Show(
                "Runner를 실행했지만 아직 연결되지 않았습니다. 잠시 후 \"Runner 실행(배포)\"를 다시 눌러 주세요.",
                "Runner 실행",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// (LK-02b 후속, ★ 사용자 요청) "파일 → Runner 중지" 클릭 핸들러입니다. 이 창(정확히는
    /// <see cref="_runnerProcessManager"/>)이 직접 띄운 프로세스만 정지할 수 있습니다 — 사용자가
    /// 터미널 등에서 직접 실행한 Runner는 이 메뉴로 끌 수 없다는 점을 안내 메시지로 알립니다
    /// (<see cref="RunnerProcessManager"/> 클래스 remarks의 "외부에서 실행한 Runner는 정지 불가" 항목 참고).
    /// </summary>
    private void OnRunnerStopClick(object sender, RoutedEventArgs e)
    {
        if (!_runnerProcessManager.IsRunning)
        {
            MessageBox.Show(
                "이 Editor가 직접 실행한 Runner 프로세스가 없습니다(외부에서 실행한 Runner는 이 메뉴로 정지할 수 없습니다).",
                "Runner 중지",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _runnerProcessManager.Stop();
    }

    /// <summary>
    /// (LK-03) "파일 → 토큰 재발급" 메뉴 클릭 핸들러입니다. Runner에 연결돼 있어야만 호출할 수
    /// 있습니다(재발급 자체가 인증된 SignalR 호출 — <c>MonitorHub.ReissueToken</c> XML 문서 참고).
    /// 성공하면 새 토큰을 <see cref="_monitorClient"/>에 즉시 반영(<see cref="EditorMonitorClient.ReissueTokenAsync"/>가
    /// 내부에서 <c>SetToken</c>까지 호출)하고 <see cref="RunnerTokenCache.SaveAsync"/>로 로컬에도
    /// 저장해, 다음 Editor 실행 때(원격 PC라면) 다시 입력할 필요가 없게 합니다.
    /// </summary>
    private async void OnReissueTokenClick(object sender, RoutedEventArgs e)
    {
        if (!_monitorClient.IsConnected)
        {
            MessageBox.Show(
                "Runner에 연결되어 있지 않아 토큰을 재발급할 수 없습니다. 먼저 연결한 뒤 다시 시도해 주세요.",
                "토큰 재발급 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var newToken = await _monitorClient.ReissueTokenAsync();
            await RunnerTokenCache.SaveAsync(FlowCanvas.DataDirectory, newToken);
            MessageBox.Show(
                "토큰이 재발급되었습니다. 이전 토큰은 즉시 무효화되었습니다.",
                "토큰 재발급",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"토큰 재발급 중 오류가 발생했습니다.\n{ex.Message}",
                "토큰 재발급 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// (LK-03) "파일 → Runner 토큰 입력" 메뉴 클릭 핸들러입니다. <see cref="Views.TokenInputDialog"/>로
    /// 사용자에게 토큰 값을 직접 입력받아(원격 PC 등 <see cref="RunnerTokenCache"/>가 자동으로 읽지
    /// 못하는 경우) <see cref="_monitorClient"/>에 반영하고 로컬 캐시에도 저장한 뒤, 곧바로
    /// 재연결(<see cref="EditorMonitorClient.StopAsync"/>→<see cref="EditorMonitorClient.StartAsync"/>)을
    /// 시도합니다 — <see cref="EditorMonitorClient.SetToken"/> 자체 문서의 "즉시 반영이 필요하면
    /// Stop 후 Start" 안내를 그대로 따릅니다.
    /// </summary>
    private async void OnEnterTokenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TokenInputDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.EnteredToken))
        {
            return;
        }

        _monitorClient.SetToken(dialog.EnteredToken);
        await RunnerTokenCache.SaveAsync(FlowCanvas.DataDirectory, dialog.EnteredToken);

        await _monitorClient.StopAsync();
        await _monitorClient.StartAsync();
    }

    /// <summary>
    /// (EC-06) "편집 → 복사" 메뉴 Click과 Ctrl+C(CommandBinding.Executed)가 공유하는 핸들러입니다.
    /// <c>FlowCanvas.CopySelectedNode()</c>를 호출해 지금 캔버스에서 선택된 노드를 내부 클립보드에
    /// 담습니다(선택된 노드가 없으면 <see cref="Views.FlowCanvasView.CopySelectedNode"/> 내부에서
    /// 아무 동작도 하지 않고 조용히 반환합니다).
    /// </summary>
    private void OnCopyNodeClick(object sender, RoutedEventArgs e) => FlowCanvas.CopySelectedNode();

    /// <summary>
    /// (EC-06) "편집 → 붙여넣기" 메뉴 Click과 Ctrl+V(CommandBinding.Executed)가 공유하는
    /// 핸들러입니다. <c>FlowCanvas.PasteNode()</c>를 호출해 내부 클립보드에 담긴 노드를 새 Id로
    /// 재발급해 지금 활성 Flow 탭에 붙여넣습니다(복사한 적이 없으면
    /// <see cref="Views.FlowCanvasView.PasteNode"/> 내부에서 아무 동작도 하지 않습니다).
    /// </summary>
    private void OnPasteNodeClick(object sender, RoutedEventArgs e) => FlowCanvas.PasteNode();

    /// <summary>
    /// (EC-07) "편집 → 실행 취소" 메뉴의 <c>Command</c> 바인딩과 Ctrl+Z가 공유하는
    /// <c>CommandBinding.Executed</c> 핸들러입니다. <c>FlowCanvas.Undo()</c>를 호출해 캔버스의
    /// 가장 최근 커맨드(노드 추가/와이어 연결/속성 편집)를 되돌립니다.
    /// </summary>
    private void OnUndoClick(object sender, ExecutedRoutedEventArgs e) => FlowCanvas.Undo();

    /// <summary>
    /// (EC-07) "편집 → 다시 실행" 메뉴의 <c>Command</c> 바인딩과 Ctrl+Y가 공유하는
    /// <c>CommandBinding.Executed</c> 핸들러입니다. <c>FlowCanvas.Redo()</c>를 호출해 Undo로
    /// 되돌렸던 커맨드를 다시 실행합니다.
    /// </summary>
    private void OnRedoClick(object sender, ExecutedRoutedEventArgs e) => FlowCanvas.Redo();

    /// <summary>
    /// (EC-07) <c>ApplicationCommands.Undo</c>의 <c>CommandBinding.CanExecute</c> 핸들러입니다.
    /// <c>FlowCanvas.CanUndo</c>가 <c>false</c>이면(되돌릴 커맨드가 없으면) "편집 → 실행 취소"
    /// 메뉴가 자동으로 회색 비활성화됩니다.
    /// </summary>
    private void OnUndoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = FlowCanvas.CanUndo;

    /// <summary>
    /// (EC-07) <c>ApplicationCommands.Redo</c>의 <c>CommandBinding.CanExecute</c> 핸들러입니다.
    /// <c>FlowCanvas.CanRedo</c>가 <c>false</c>이면(다시 실행할 커맨드가 없으면) "편집 → 다시 실행"
    /// 메뉴가 자동으로 회색 비활성화됩니다.
    /// </summary>
    private void OnRedoCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = FlowCanvas.CanRedo;

    /// <summary>
    /// (EC-10) "편집 → 그룹으로 묶기" 메뉴 Click과 Ctrl+G(<see cref="EditorCommands.GroupNodes"/>의
    /// <c>CommandBinding.Executed</c>)가 공유하는 핸들러입니다. <c>FlowCanvas.GroupSelectedNodes()</c>를
    /// 호출해 지금 Ctrl+클릭으로 선택된 노드들을 새 그룹으로 묶습니다(2개 미만이면 아무 동작도
    /// 하지 않습니다).
    /// </summary>
    private void OnGroupNodesClick(object sender, RoutedEventArgs e) => FlowCanvas.GroupSelectedNodes();

    /// <summary>
    /// (EC-10) "편집 → 그룹 해제" 메뉴 Click과 Ctrl+Shift+G(<see cref="EditorCommands.UngroupNodes"/>의
    /// <c>CommandBinding.Executed</c>)가 공유하는 핸들러입니다. <c>FlowCanvas.UngroupSelectedGroup()</c>를
    /// 호출해 지금 선택된 노드가 속한 그룹을 해제합니다(선택이 없거나 어떤 그룹에도 속하지 않으면
    /// 아무 동작도 하지 않습니다).
    /// </summary>
    private void OnUngroupNodesClick(object sender, RoutedEventArgs e) => FlowCanvas.UngroupSelectedGroup();

    /// <summary>
    /// (ED-B3) 창 상태가 바뀔 때마다 최대화/복원 버튼 아이콘을 맞는 모양으로 바꾸고, 최대화 상태일
    /// 때는 <see cref="SystemParameters.WorkArea"/>(작업표시줄을 제외한 화면 영역) 크기로 최대
    /// 크기를 제한합니다 — <c>WindowStyle="None"</c>+<c>WindowChrome</c> 조합에서 최대화하면 창이
    /// 작업표시줄까지 덮어버리는 잘 알려진 WPF 문제를 인터롭 없이 간단히 방지하는 방법입니다.
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";

        if (WindowState == WindowState.Maximized)
        {
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
        }
    }
}
