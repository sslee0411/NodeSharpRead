using System.Windows;
using NodeSharp.Editor.Views;

namespace NodeSharp.Editor.Core;

/// <summary>
/// Class명 : 시퀀스 창 매니저
/// 역활 및 기능 : SequenceEditorWindow 인스턴스를 중복 없이 하나만 유지하고, 마지막 위치·크기·모니터를 기억해 다시 열 때 복원하며, "호출하는 Flow 노드 보기" 요청을 캔버스 하이라이트로 연결하는 정적 매니저
///
/// (SQ-02) 03번 Step맵 완료 기준 "두 번 열어도 중복 창이 생기지 않고, 다른 모니터로 이동해도 위치가
/// 유지되는지"를 담당하는 클래스입니다. <see cref="MainWindow"/>가 <see cref="SequenceEditorWindow"/>를
/// 직접 <c>new</c>하지 않고 이 매니저의 <see cref="ShowOrActivate"/>만 호출하도록 해, "이미 열려있으면
/// 그 창을 앞으로 가져온다"/"위치를 기억한다"는 책임을 창 자체(<see cref="SequenceEditorWindow"/>)가
/// 아니라 이 매니저에 모아뒀습니다.
/// (SQ-03, ★ 보강) 완료 기준의 "역방향"(호출하는 Flow 노드 보기 → 캔버스 하이라이트)도 이 매니저가
/// 잇습니다 — <see cref="SequenceEditorWindow.FindCallersRequested"/>를 구독해
/// <c>MainWindow.FlowCanvas</c>(<see cref="IFlowNodeIndex"/> 구현)로 조회한 뒤
/// <c>FlowCanvasView.NavigateToNode</c>(EC-12, 기존 메서드 재사용)로 캔버스를 전환·하이라이트합니다.
/// (SQ-04, ★ 보강) 창을 새로 만들 때 <see cref="SequenceEditorWindow.History"/>에
/// <c>owner.FlowCanvas.History</c>(EC-07 공유 CommandHistory)를, <see cref="SequenceEditorWindow.DataDirectory"/>에
/// <c>owner.FlowCanvas.DataDirectory</c>를 그대로 채워줍니다 — <c>StructureTab.History = FlowCanvas.History;</c>
/// (ED-D13)와 동일한 배선을 이 매니저의 창 생성 지점 한 곳에 모아둔 것입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>중복 방지</b>: <see cref="_instance"/> 정적 필드 하나로 "이미 열려있는 창"을 추적합니다.
/// 이미 있으면 <see cref="Window.Activate"/>로 포커스만 주고(최소화 상태면 <see cref="WindowState.Normal"/>로
/// 되돌린 뒤) 새로 만들지 않습니다. <see cref="Window.Closed"/>에서 필드를 <c>null</c>로 되돌려 다음
/// <see cref="ShowOrActivate"/> 호출이 새 인스턴스를 만들 수 있게 합니다.</item>
/// <item><b>위치 기억 범위(★ 판단)</b>: "다른 모니터로 이동해도 위치가 유지되는지"라는 완료 기준은 앱을
/// 껐다 켜도 유지되는지까지는 요구하지 않는다고 판단해(재기동 후 복원까지 필요했다면 파일 저장
/// 인프라가 필요 — <c>FileContextStore</c>(RT-09c)류의 새 설정 파일이 생기므로 별도 판단이 필요한
/// 확장), 이 세션(프로세스) 안에서만 유지되는 정적 필드(<see cref="_lastBounds"/>/<see cref="_lastState"/>)로
/// 최소 구현했습니다 — 창을 닫았다 같은 세션에서 다시 열면 마지막 위치·크기·모니터가 그대로 복원됩니다.</item>
/// <item><b>화면 밖 방지</b>: 기억해둔 위치가 모니터 구성이 바뀌어(예: 두 번째 모니터 연결 해제) 더는
/// 어떤 화면에도 걸치지 않으면 <see cref="SystemParameters"/>의 가상 화면(전체 모니터를 아우르는 좌표계)
/// 기준으로 감지해 무시하고, 대신 Owner(MainWindow) 중앙에 새로 배치합니다.</item>
/// <item><b>(SQ-03) 다중 결과 범위 축소</b>: <c>FindNodesBySequenceId</c>가 여러 개를 반환하면(같은
/// 시퀀스를 여러 곳에서 호출) <see cref="NodeRef"/> XML 문서 예시가 제안한 "드롭다운으로 선택" 대신
/// 첫 번째 결과로 이동합니다 — 완료 기준은 "하이라이트가 동작하는지"만 요구해 다중 선택 UI는 후속
/// Step으로 미뤘습니다(찾은 개수는 MessageBox로 함께 알려줍니다).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // MainWindow.xaml.cs — OnSequenceEditorEntryClick(메뉴/네비게이션 진입점, "편도" 기본형)
/// private void OnSequenceEditorEntryClick(object sender, RoutedEventArgs e) =&gt;
///     SequenceWindowManager.ShowOrActivate(this);
///
/// // FlowCanvasView.OnCardMouseLeftButtonDown — SequenceTriggerNode 더블클릭("편도", sequenceId 전달)
/// SequenceWindowManager.ShowOrActivate(mainWindow, initialSequenceId: node.SequenceId);
/// </code>
/// </example>
public static class SequenceWindowManager
{
    private static SequenceEditorWindow? _instance;
    private static Rect? _lastBounds;
    private static WindowState _lastState = WindowState.Normal;

    /// <summary>
    /// 이미 열려있는 <see cref="SequenceEditorWindow"/>가 있으면 그 창을 앞으로 가져오고, 없으면 새로
    /// 만들어 <paramref name="owner"/>를 소유자로 띄웁니다(마지막으로 기억해둔 위치·크기·모니터가 있고
    /// 여전히 화면 안이면 그대로 복원, 아니면 <paramref name="owner"/> 중앙에 배치). <paramref name="initialSequenceId"/>가
    /// 주어지면(예: SequenceTriggerNode 더블클릭) 창의 "시퀀스 Id" 입력란을 그 값으로 채웁니다(이미
    /// 열려있던 창이면 값을 덮어씁니다 — 사용자가 방금 더블클릭한 노드를 보러 온 것이므로).
    /// </summary>
    public static void ShowOrActivate(MainWindow owner, string? initialSequenceId = null)
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            if (initialSequenceId is not null)
            {
                _instance.SetSequenceId(initialSequenceId);
            }

            _instance.Activate();
            return;
        }

        var window = new SequenceEditorWindow
        {
            Owner = owner,
            // (SQ-04) StructureTab.History = FlowCanvas.History;(위 클래스 주석 ED-D13 단락)와 동일한
            // 배선 — 시퀀스 단계 추가/삭제 커맨드도 캔버스·구조 트리와 같은 CommandHistory 스택을 쓴다.
            History = owner.FlowCanvas.History,
            // (SQ-04) sequences.json도 flows.json/device.json과 같은 데이터 폴더에 둔다.
            DataDirectory = owner.FlowCanvas.DataDirectory,
        };
        if (initialSequenceId is not null)
        {
            window.SetSequenceId(initialSequenceId);
        }

        window.FindCallersRequested += sequenceId => OnFindCallersRequested(owner, sequenceId);
        ApplyRememberedBounds(window, owner);
        window.Closing += (_, _) => RememberBounds(window);
        window.Closed += (_, _) => _instance = null;

        _instance = window;
        window.Show();
    }

    private static void OnFindCallersRequested(MainWindow owner, string sequenceId)
    {
        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            MessageBox.Show(owner, "시퀀스 Id를 먼저 입력하세요.", "호출하는 Flow 노드 보기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var callers = owner.FlowCanvas.FindNodesBySequenceId(sequenceId);
        if (callers.Count == 0)
        {
            MessageBox.Show(owner, $"시퀀스 '{sequenceId}'를 호출하는 노드를 찾지 못했습니다.", "호출하는 Flow 노드 보기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var first = callers[0];
        owner.FlowCanvas.NavigateToNode(first.FlowId, first.NodeId);
        owner.Activate();

        if (callers.Count > 1)
        {
            MessageBox.Show(owner, $"이 시퀀스를 호출하는 노드가 {callers.Count}개 있습니다. 첫 번째('{first.NodeName}')로 이동했습니다.", "호출하는 Flow 노드 보기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void ApplyRememberedBounds(Window window, Window owner)
    {
        if (_lastBounds is { } bounds && IsOnVisibleVirtualScreen(bounds))
        {
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.WindowState = _lastState == WindowState.Minimized ? WindowState.Normal : _lastState;
            return;
        }

        window.Left = owner.Left + ((owner.Width - window.Width) / 2);
        window.Top = owner.Top + ((owner.Height - window.Height) / 2);
    }

    private static void RememberBounds(Window window)
    {
        _lastState = window.WindowState;
        _lastBounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
    }

    private static bool IsOnVisibleVirtualScreen(Rect bounds)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var probe = new Rect(bounds.Left, bounds.Top, System.Math.Min(bounds.Width, 100), System.Math.Min(bounds.Height, 60));
        return virtualScreen.IntersectsWith(probe);
    }
}
