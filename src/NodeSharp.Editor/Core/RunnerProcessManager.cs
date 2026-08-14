using System.Diagnostics;
using System.IO;

namespace NodeSharp.Editor.Core;

/// <summary>
/// Class명 : Runner 프로세스 관리자
/// 역활 및 기능 : Editor 안에서 NodeSharp.Runner 실행 파일을 자식 프로세스로 실행/정지하고, 그 경로를 다음 실행을 위해 기억하는 클래스
///
/// (LK-02b 후속, ★ 사용자 요청) "기존 Node-RED처럼 편집+실행이 한 곳에서 도는 경험을 원하지만, Runner
/// 자체는 Editor가 끝난 뒤 배포 형식(실행 파일처럼)으로 동작했으면 한다"는 요청에 따라 만들어졌습니다.
/// 이 저장소는 처음부터 Editor(WPF)와 Runner(헤드리스 서비스, README "빌드·테스트 확인 방법" 문서의
/// "헤드리스 프로젝트" 그룹)를 완전히 별도 프로세스로 설계했습니다 — 이 결정은 되돌리지 않았습니다
/// (Runner를 산업 현장 PC/게이트웨이에 Editor 없이 헤드리스로 배포할 수 있어야 한다는 이 프로젝트의
/// IIoT 목표와 직결되므로, 두 프로세스를 하나로 합치는 재설계는 이번 요청 범위 밖이라고 판단). 대신
/// 이 클래스가 "Editor 안에서 버튼 한 번으로 Runner를 띄우고 끌 수 있게" 해, 사용자 체감상으로는
/// Node-RED의 Deploy 버튼과 비슷한 경험을 주면서도 배포 아키텍처는 그대로 유지합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>경로 기억</b>: 사용자가 최초 1회 Runner 실행 파일(.exe 또는 .dll)을 선택하면
/// <see cref="SavePathAsync"/>가 <c>runner.path.txt</c>(평문 1줄, flows.json과 같은 폴더)에 저장하고,
/// <see cref="LoadPathAsync"/>가 다음 Editor 실행 시 자동으로 불러옵니다 — <c>FlowStore</c>처럼
/// 원자적 저장(.tmp→File.Replace)까지는 하지 않습니다(설정 파일 1줄이라 손상 위험이 낮고, 손상돼도
/// 다음에 다시 물어보면 그만이라 flows.json만큼 엄격할 필요가 없다고 판단).</item>
/// <item><b>.dll이면 <c>dotnet</c>으로 실행</b>: 배포 산출물이 self-contained exe가 아니라 프레임워크
/// 종속 .dll일 수 있어(예: <c>dotnet build</c> 직후의 <c>bin\Debug\net8.0\NodeSharp.Runner.dll</c>),
/// 확장자가 <c>.dll</c>이면 <c>dotnet "그 경로"</c>로, 아니면(<c>.exe</c> 등) 그 파일을 직접 실행합니다.</item>
/// <item><b>Editor 종료 시 Runner를 함께 끄지 않음</b>: 사용자가 원한 "배포 형식(실행 파일처럼) 동작"은
/// Editor와 독립적으로 계속 떠 있어야 한다는 뜻이라고 해석해, <c>MainWindow.OnWindowClosed</c>에서
/// 이 프로세스를 자동으로 <see cref="Stop"/>하지 않습니다 — 정지는 "Runner 중지" 메뉴로 사용자가
/// 명시적으로 눌러야만 일어납니다.</item>
/// <item><b>외부에서 실행한 Runner는 정지 불가</b>: <see cref="IsRunning"/>은 이 클래스가 직접
/// <see cref="Start"/>로 띄운 프로세스만 추적합니다 — 사용자가 터미널에서 직접 <c>dotnet run</c>한
/// Runner는 이 클래스가 <see cref="Process"/> 핸들을 가지고 있지 않아 <see cref="Stop"/>으로 끌 수
/// 없습니다(연결 상태 배지로는 "연결됨"이 보이지만 "Runner 중지" 메뉴를 누르면 안내 메시지만 뜸).</item>
/// </list>
/// </remarks>
public sealed class RunnerProcessManager
{
    private const string SettingsFileName = "runner.path.txt";

    private Process? _process;

    /// <summary>사용자가 선택했거나 <see cref="LoadPathAsync"/>로 불러온 Runner 실행 파일 경로입니다. 아직 없으면 <c>null</c>.</summary>
    public string? RunnerExecutablePath { get; set; }

    /// <summary>이 클래스가 <see cref="Start"/>로 직접 띄운 프로세스가 아직 살아있으면 <c>true</c>입니다(위 클래스 remarks "외부에서 실행한 Runner는 정지 불가" 참고).</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary><paramref name="dataDirectory"/>\runner.path.txt가 있으면 읽어 <see cref="RunnerExecutablePath"/>를 채웁니다. 없으면 아무 일도 하지 않습니다.</summary>
    public async Task LoadPathAsync(string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, SettingsFileName);
        if (File.Exists(path))
        {
            var text = await File.ReadAllTextAsync(path, ct);
            RunnerExecutablePath = text.Trim();
        }
    }

    /// <summary><see cref="RunnerExecutablePath"/>가 채워져 있으면 <paramref name="dataDirectory"/>\runner.path.txt에 저장합니다.</summary>
    public async Task SavePathAsync(string dataDirectory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(RunnerExecutablePath))
        {
            return;
        }

        var path = Path.Combine(dataDirectory, SettingsFileName);
        await File.WriteAllTextAsync(path, RunnerExecutablePath, ct);
    }

    /// <summary>
    /// <see cref="RunnerExecutablePath"/>를 자식 프로세스로 실행합니다. 이미 이 클래스가 띄운 프로세스가
    /// 살아있거나 경로가 비어 있으면 아무 일도 하지 않습니다(중복 실행 방지).
    /// </summary>
    public void Start()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(RunnerExecutablePath))
        {
            return;
        }

        var isDll = RunnerExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = isDll
            ? new ProcessStartInfo("dotnet", $"\"{RunnerExecutablePath}\"")
            : new ProcessStartInfo(RunnerExecutablePath);

        startInfo.UseShellExecute = false;
        startInfo.WorkingDirectory = Path.GetDirectoryName(RunnerExecutablePath) ?? AppContext.BaseDirectory;

        _process = Process.Start(startInfo);
    }

    /// <summary>
    /// <see cref="Start"/>로 띄운 프로세스를 종료합니다(자식 프로세스까지 포함, <c>dotnet</c>으로 실행한
    /// 경우 실제 Runner는 그 자식이므로). 이 클래스가 띄운 프로세스가 없으면 아무 일도 하지 않습니다.
    /// </summary>
    public void Stop()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 그 사이 이미 종료된 경우 — 조용히 무시(공통 규칙, FlowFileWatcher의 "콜백 예외 격리"와 동일한 정신).
        }

        _process = null;
    }
}
