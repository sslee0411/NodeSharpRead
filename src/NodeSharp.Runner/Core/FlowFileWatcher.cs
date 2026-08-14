namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 플로우 파일 감시기
/// 역활 및 기능 : flows.json.signal 파일 변경을 FileSystemWatcher로 감지해 디바운스 후 재배포 콜백을 호출하는 클래스
///
/// (LK-01) Editor(<c>NodeSharp.Editor.Core.Config.JsonWriteService.WriteSignalAsync</c>, EC-04)가
/// flows.json을 저장할 때마다 남기는 <c>flows.json.signal</c> 파일 변경을 <see cref="FileSystemWatcher"/>로
/// 감지해, 등록된 콜백(보통 <c>FlowDeployer.RedeployAsync</c>)을 호출합니다. 02번 설계 문서 1번 탭
/// 폴더 구조가 지정한 <c>NodeSharp.Runner\Core\FlowFileWatcher.cs</c> 위치 그대로입니다.
/// 설계 근거: 02번 문서 1번 탭 폴더 구조, 4번 탭 카드1(Editor/Runner 분리 다이어그램 — "B --
/// FileSystemWatcher 감지 --&gt; C[Runner]"), 03번 개발 Step맵 Phase 8 LK-01.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>디바운스가 필요한 이유</b>: Windows의 <see cref="FileSystemWatcher"/>는 파일 하나를
/// 한 번 저장해도 내부 버퍼링·OS 이벤트 특성상 <see cref="FileSystemWatcher.Changed"/>가 여러 번
/// 연달아 발생할 수 있습니다(잘 알려진 동작) — 이 클래스는 원시 이벤트가 올 때마다
/// <see cref="System.Threading.Timer"/>를 다시 예약(reset)하는 방식으로, "마지막 이벤트 이후
/// <paramref name="debounce"/> 시간 동안 조용하면 그때 1번만" 콜백을 호출합니다.</item>
/// <item><b>재진입 방지</b>: 콜백(재배포)이 아직 끝나지 않았는데 그사이 새 신호가 또 오면, 진행 중인
/// 콜백과 겹쳐서 동시에 두 번 배포하지 않도록 <see cref="Interlocked.CompareExchange(ref int, int, int)"/>로
/// 가드합니다 — 겹친 신호는 그냥 버려집니다(데이터 손실 아님: 재배포는 매번 flows.json 전체를 다시
/// 읽으므로, 지금 처리 중인 배포가 끝난 뒤 사용자가 다시 저장하면 그 최신 내용은 다음 신호로 다시
/// 잡힙니다).</item>
/// <item><b>콜백 예외 격리</b>: <paramref name="onSignal"/>이 예외를 던져도 이 클래스가 삼켜
/// <see cref="FileSystemWatcher"/> 자체나 프로세스가 죽지 않게 합니다 — <c>Worker</c>의 5분 주기
/// 루프(RN-05a/RN-05b-a)가 개별 확인 실패를 격리하는 것과 동일한 원칙입니다. 콘솔에 실패 사실만
/// 한 줄 남겨(<see cref="NodeStatusConsoleLogger"/>와 동일한 "헤드리스 콘솔이 1차 가시성 채널"
/// 원칙) 다음 신호에서 재시도됨을 알 수 있게 합니다.</item>
/// <item><b>파일 존재 여부와 무관하게 생성 가능</b>: <c>flows.json.signal</c>이 아직 한 번도
/// 만들어지지 않은 상태(최초 실행, Editor가 아직 저장 전)에서도 <see cref="FileSystemWatcher"/>는
/// 문제없이 감시를 시작합니다 — 나중에 파일이 처음 생성될 때 <see cref="FileSystemWatcher.Created"/>
/// 이벤트로 잡힙니다(<see cref="FileSystemWatcher.Changed"/>만 구독하면 최초 생성을 놓칠 수 있어
/// 두 이벤트 모두 구독).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// FlowEngine? engine = ...; // 부팅 시 이미 배포됐을 수도, 아직 null일 수도 있음
/// var deployer = new FlowDeployer();
/// using var watcher = new FlowFileWatcher(baseDirectory, async ct =>
/// {
///     engine = await deployer.RedeployAsync(engine, baseDirectory, registry, ct);
/// });
/// // 이후 Editor가 flows.json을 저장할 때마다(=flows.json.signal 갱신) 위 콜백이 자동 호출됨
/// </code>
/// </example>
public sealed class FlowFileWatcher : IDisposable
{
    private const string SignalFileName = "flows.json.signal";

    private readonly FileSystemWatcher _watcher;
    private readonly Func<CancellationToken, Task> _onSignal;
    private readonly TimeSpan _debounce;
    private readonly object _timerLock = new();
    private Timer? _debounceTimer;
    private int _isHandling; // Interlocked 가드 — 0: 유휴, 1: 콜백 실행 중

    /// <summary>
    /// <paramref name="directory"/>(보통 Runner 실행 파일 폴더, flows.json이 있는 곳)의
    /// <c>flows.json.signal</c> 변경을 감시합니다. <paramref name="debounce"/>를 생략하면 300ms를
    /// 씁니다(위 클래스 remarks 참고) — 테스트에서는 더 짧게 줘서 빠르게 검증합니다.
    /// </summary>
    public FlowFileWatcher(string directory, Func<CancellationToken, Task> onSignal, TimeSpan? debounce = null)
    {
        _onSignal = onSignal;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);

        Directory.CreateDirectory(directory); // 폴더 자체가 아직 없으면 FileSystemWatcher 생성자가 예외를 던짐 — 최초 실행 보호.

        _watcher = new FileSystemWatcher(directory, SignalFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
        };
        _watcher.Changed += OnRawEvent;
        _watcher.Created += OnRawEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// 원시 <see cref="FileSystemWatcher"/> 이벤트마다 호출됩니다. 진행 중이던 디바운스 타이머가
    /// 있으면 취소하고 새로 <see cref="_debounce"/> 뒤에 <see cref="Fire"/>가 실행되도록 다시
    /// 예약합니다 — 짧은 시간 안에 여러 번 오는 원시 이벤트를 1번의 <see cref="Fire"/> 호출로
    /// 합칩니다(위 클래스 remarks의 "디바운스가 필요한 이유" 참고).
    /// </summary>
    private void OnRawEvent(object sender, FileSystemEventArgs e)
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => Fire(), state: null, dueTime: _debounce, period: Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>디바운스가 끝나면 호출됩니다. 이미 처리 중이면(<see cref="_isHandling"/>) 이번 신호는 조용히 버립니다(위 클래스 remarks의 "재진입 방지" 참고).</summary>
    private void Fire()
    {
        if (Interlocked.CompareExchange(ref _isHandling, 1, 0) != 0)
        {
            return;
        }

        _ = HandleAsync();
    }

    /// <summary><see cref="_onSignal"/>을 호출하고, 예외가 나도 삼켜 콘솔에만 남깁니다(위 클래스 remarks의 "콜백 예외 격리" 참고). 끝나면 반드시 <see cref="_isHandling"/>을 되돌려 다음 신호를 받을 수 있게 합니다.</summary>
    private async Task HandleAsync()
    {
        try
        {
            await _onSignal(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] flows.json.signal 재배포 처리 중 오류 — 다음 신호에서 재시도됩니다: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isHandling, 0);
        }
    }

    /// <summary>감시를 멈추고 <see cref="FileSystemWatcher"/>·디바운스 타이머 리소스를 해제합니다.</summary>
    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnRawEvent;
        _watcher.Created -= OnRawEvent;
        _watcher.Dispose();

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
        }
    }
}
