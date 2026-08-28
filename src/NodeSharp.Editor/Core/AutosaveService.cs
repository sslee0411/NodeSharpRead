using System.Windows;
using System.Windows.Threading;
using NodeSharp.Editor.Views;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;
using Timer = System.Timers.Timer;
using System.IO;

namespace NodeSharp.Editor.Core;

/// <summary>
/// Class명 : 자동저장 · 크래시 복구 서비스
/// 역활 및 기능 : 정식 저장(flows.json/device.json)과는 별개로 30초마다 편집 중인 내용을 .autosave
/// 폴더에 스냅샷으로 남기고, Editor가 비정상 종료된 흔적이 있으면 다음 시작 시 복구를 제안하는 서비스
///
/// (ED-D14, ★ 완료 기준 — "30초 경과 시 스냅샷이 생성되고, 비정상 종료 후 재기동 시 복구 다이얼로그가
/// 표시되는지 확인") 02번 설계문서 8번 탭 카드17이 원안대로 요구한 <c>EditorSessionState</c>(Flow/
/// 구조/Sequence/Dashboard 4개 Dirty 플래그를 한 곳에서 추적하는 통합 세션 상태)는 이 프로젝트에
/// Sequence/Dashboard 화면 자체가 아직 없어(Phase 11 이후 예정) 그대로 만들 수 없었습니다 — 대신
/// <see cref="FlowCanvasView"/>/<see cref="StructureView"/>가 ED-D13에서 이미 갖춘 "마지막 저장
/// 내용과 지금 직렬화 결과를 비교"하는 더티 판정(<c>_lastSavedFlowsJson</c>/<c>_lastSavedDeviceJson</c>)을
/// 그대로 재사용하는 <see cref="FlowCanvasView.GetAutosaveSnapshotIfDirty"/>/
/// <see cref="StructureView.GetAutosaveSnapshotIfDirty"/>로 "지금 편집 중인데 아직 정식 저장 안 된
/// 내용이 있는가"만 필요한 만큼 좁혀 구현했습니다(카드17 의사코드의 <c>session.HasUnsavedChanges</c>
/// 체크와 동일한 목적, Flow/구조 2종만 지원 — Sequence/Dashboard는 그 화면이 생기는 후속 Step에서
/// 동일 패턴으로 자연스럽게 추가 가능).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>주기 30초</b>: 카드17 의사코드와 동일하게 30초 주기 — 두 뷰 모두 변경이 없으면
/// (<see cref="FlowCanvasView.GetAutosaveSnapshotIfDirty"/>/<see cref="StructureView.GetAutosaveSnapshotIfDirty"/>
/// 둘 다 <c>null</c>) 디스크 I/O 자체를 건너뜁니다.</item>
/// <item><b>UI 스레드 경계</b>: <see cref="Timer.Elapsed"/>는 스레드 풀 스레드에서 발생하는데, 두
/// 뷰의 내부 컬렉션(<c>_nodeConfigs</c>/<c>Devices</c> 등)은 UI 스레드 전용이라 직접 건드리면
/// 경합이 생깁니다 — <see cref="RunOnUiThread"/>로 스냅샷 문자열만 UI 스레드에서 뽑아온 뒤, 실제
/// 파일 I/O는 스레드 풀 스레드에서 계속합니다(<c>MainWindow.SafeDispatcherInvoke</c>와 동일하게
/// "창이 닫히는 중이면 조용히 건너뛴다" 방어를 그대로 반영).</item>
/// <item><b>원본과 완전히 분리</b>: <c>.autosave\flows.autosave.json</c>/<c>device.autosave.json</c>은
/// 정식 저장 경로(<see cref="FlowCanvasView.SaveFlowAsync"/>/<see cref="StructureView.SaveDeviceTreeAsync"/>,
/// 원자적 저장 — <c>JsonWriteService.WriteAtomicAsync</c>)와 완전히 다른 파일이라, 자동저장이 실수로
/// 원본을 덮어쓸 위험이 없습니다(카드17 표 "원본과의 관계"). 자동저장 자체는 원자적 쓰기까지는 하지
/// 않습니다(<see cref="File.WriteAllTextAsync(string, string)"/> 그대로) — 쓰다가 크래시가 나도
/// 다음 시작 시 그 손상된 파일은 <see cref="CheckAndPromptRecovery"/> 이후의 JSON 파싱이 실패로
/// 조용히 원본 로드 폴백을 타므로(<see cref="FlowCanvasView.PendingAutosaveOverrideJson"/>/
/// <see cref="StructureView.PendingAutosaveOverrideJson"/> 각 클래스 주석 참고), 원자적 쓰기가
/// 아니어도 최악의 경우가 "이번 자동저장 1회를 못 씀"에 그쳐 손해가 크지 않다고 판단.</item>
/// <item><b>정상 종료 시 정리</b>: <c>MainWindow.OnWindowClosed</c>(<see cref="Window.Closed"/>)가
/// <see cref="ClearOnCleanExit"/>를 호출해 <c>.autosave</c> 파일을 지웁니다 — 다음 시작 때
/// <see cref="CheckAndPromptRecovery"/>가 복구 다이얼로그를 띄우지 않습니다(카드17 표 "정상 종료
/// 시" 항목).</item>
/// </list>
/// </remarks>
public sealed class AutosaveService : IDisposable
{
    private const string FlowAutosaveFileName = "flows.autosave.json";
    private const string DeviceAutosaveFileName = "device.autosave.json";
    private const string AutosaveFolderName = ".autosave";

    private readonly FlowCanvasView _flowCanvas;
    private readonly StructureView _structureView;
    private readonly Dispatcher _dispatcher;
    private readonly Timer _timer = new(TimeSpan.FromSeconds(30).TotalMilliseconds);

    /// <summary>
    /// <paramref name="flowCanvas"/>/<paramref name="structureView"/>는 각각 <c>MainWindow</c>의
    /// <c>FlowCanvas</c>/<c>StructureTab</c> 그대로이고, <paramref name="dispatcher"/>는 그 창의
    /// <see cref="Window.Dispatcher"/>입니다(<see cref="Timer"/> 콜백이 스레드 풀에서 오므로 UI
    /// 스레드로 다시 넘길 때 필요 — 클래스 remarks의 "UI 스레드 경계" 참고).
    /// </summary>
    public AutosaveService(FlowCanvasView flowCanvas, StructureView structureView, Dispatcher dispatcher)
    {
        _flowCanvas = flowCanvas;
        _structureView = structureView;
        _dispatcher = dispatcher;
        _timer.Elapsed += OnTimerElapsed;
    }

    /// <summary>30초 주기 자동저장을 시작합니다 — <c>MainWindow</c> 생성자가 <see cref="CheckAndPromptRecovery"/> 호출 직후에 부릅니다.</summary>
    public void Start() => _timer.Start();

    /// <summary>
    /// <c>MainWindow</c> 생성자가 <see cref="Start"/> 호출 전에 딱 한 번 부릅니다 — 지난 세션의
    /// <c>.autosave</c> 스냅샷이 원본(flows.json/device.json)보다 최신이면(=비정상 종료 흔적, 카드17
    /// 의사코드의 <c>AutosaveFolder.HasNewerSnapshotThan</c>) 복구 여부를 묻는 모달을 띄웁니다.
    /// "복구"를 선택하면 <see cref="FlowCanvasView.PendingAutosaveOverrideJson"/>/
    /// <see cref="StructureView.PendingAutosaveOverrideJson"/>을 채워, 그 직후(아직 발생 전인) 각
    /// 뷰의 <c>Loaded</c>가 원본 대신 이 내용을 적용하도록 합니다 — 두 뷰 모두 <c>Loaded</c>는
    /// <c>MainWindow</c> 생성자가 끝난 뒤에야 발생하므로, 생성자 안에서 부르는 이 메서드가 항상 먼저
    /// 끝나 있습니다. "무시"를 선택하면 다음에 또 묻지 않도록 <c>.autosave</c> 파일을 바로 지웁니다.
    /// </summary>
    public void CheckAndPromptRecovery()
    {
        var autosaveDir = Path.Combine(_flowCanvas.DataDirectory, AutosaveFolderName);
        var flowsAutosavePath = Path.Combine(autosaveDir, FlowAutosaveFileName);
        var deviceAutosavePath = Path.Combine(autosaveDir, DeviceAutosaveFileName);

        var flowSnapshotIsNewer = IsNewerThanOriginal(flowsAutosavePath, Path.Combine(_flowCanvas.DataDirectory, "flows.json"));
        var deviceSnapshotIsNewer = IsNewerThanOriginal(deviceAutosavePath, Path.Combine(_flowCanvas.DataDirectory, "device.json"));

        if (!flowSnapshotIsNewer && !deviceSnapshotIsNewer)
        {
            return;
        }

        var parts = new List<string>();
        if (flowSnapshotIsNewer)
        {
            parts.Add("Flow(flows.json)");
        }

        if (deviceSnapshotIsNewer)
        {
            parts.Add("구조 설정(device.json)");
        }

        var choice = MessageBox.Show(
            $"이전 작업 중 예기치 않게 종료된 흔적이 있습니다.\n자동저장된 내용({string.Join(", ", parts)})을 복구하시겠습니까?\n\n" +
            "[예] 자동저장 내용을 불러옵니다 — 확인 후 직접 저장(Ctrl+S)해야 flows.json/device.json 원본에 반영됩니다.\n" +
            "[아니오] 자동저장 내용을 버리고 원본 파일을 그대로 불러옵니다.",
            "자동저장 복구",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            if (flowSnapshotIsNewer)
            {
                _flowCanvas.PendingAutosaveOverrideJson = TryReadFile(flowsAutosavePath);
            }

            if (deviceSnapshotIsNewer)
            {
                _structureView.PendingAutosaveOverrideJson = TryReadFile(deviceAutosavePath);
            }
        }
        else
        {
            TryDelete(flowsAutosavePath);
            TryDelete(deviceAutosavePath);
        }
    }

    /// <summary>
    /// <c>MainWindow.OnWindowClosed</c>가 호출합니다 — 정상적으로 닫혔으므로 <c>.autosave</c> 파일을
    /// 지워 다음 시작 때 <see cref="CheckAndPromptRecovery"/>가 복구를 제안하지 않게 합니다(카드17
    /// 표 "정상 종료 시" 항목).
    /// </summary>
    public void ClearOnCleanExit()
    {
        var autosaveDir = Path.Combine(_flowCanvas.DataDirectory, AutosaveFolderName);
        TryDelete(Path.Combine(autosaveDir, FlowAutosaveFileName));
        TryDelete(Path.Combine(autosaveDir, DeviceAutosaveFileName));
    }

    /// <summary>
    /// 30초마다 발생 — 두 뷰의 더티 스냅샷을 UI 스레드에서 뽑아온 뒤(클래스 remarks의 "UI 스레드
    /// 경계" 참고) 둘 다 <c>null</c>(변경 없음)이면 아무 것도 하지 않고, 하나라도 있으면
    /// <c>.autosave</c> 폴더에 각각 기록합니다. <c>FlowFileWatcher</c>(LK-01)의 콜백 예외 격리와
    /// 동일한 이유로 예외를 전부 삼켜 콘솔에만 남깁니다 — 자동저장 1회 실패가 앱을 죽이거나 정식
    /// 저장을 방해해서는 안 됩니다.
    /// </summary>
    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            string? flowJson = null;
            string? deviceJson = null;

            RunOnUiThread(() =>
            {
                flowJson = _flowCanvas.GetAutosaveSnapshotIfDirty();
                deviceJson = _structureView.GetAutosaveSnapshotIfDirty();
            });

            if (flowJson is null && deviceJson is null)
            {
                return;
            }

            var autosaveDir = Path.Combine(_flowCanvas.DataDirectory, AutosaveFolderName);
            Directory.CreateDirectory(autosaveDir);

            if (flowJson is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(autosaveDir, FlowAutosaveFileName), flowJson);
            }

            if (deviceJson is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(autosaveDir, DeviceAutosaveFileName), deviceJson);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] 자동저장 실패 — 다음 주기(30초 후)에 재시도됩니다: {ex.Message}");
        }
    }

    /// <summary><c>MainWindow.SafeDispatcherInvoke</c>와 동일한 방어 — 창이 닫히는 중이면 조용히 건너뜁니다.</summary>
    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _dispatcher.Invoke(action);
        }
        catch (Exception)
        {
            // MainWindow.SafeDispatcherInvoke와 동일한 이유 — 조용히 무시.
        }
    }

    /// <summary><paramref name="autosavePath"/>가 있고, <paramref name="originalPath"/>가 없거나 그보다 최신 쓰기 시각이면 true(=비정상 종료 흔적).</summary>
    private static bool IsNewerThanOriginal(string autosavePath, string originalPath)
    {
        if (!File.Exists(autosavePath))
        {
            return false;
        }

        if (!File.Exists(originalPath))
        {
            return true; // 원본조차 없는데 autosave만 있으면(최초 실행 중 크래시) 복구 후보로 취급.
        }

        return File.GetLastWriteTimeUtc(autosavePath) > File.GetLastWriteTimeUtc(originalPath);
    }

    /// <summary>읽기 실패(디스크 오류 등)는 <c>null</c>로 흡수 — 호출자가 원본 로드로 폴백합니다.</summary>
    private static string? TryReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>삭제 실패는 치명적이지 않음(다음 자동저장 때 다시 덮어써짐) — 조용히 무시합니다.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 위 요약 참고 — 조용히 무시.
        }
    }

    /// <summary>타이머를 멈추고 리소스를 해제합니다 — <c>MainWindow.OnWindowClosed</c>가 <see cref="ClearOnCleanExit"/> 전에 호출합니다.</summary>
    public void Dispose()
    {
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Stop();
        _timer.Dispose();
    }
}
