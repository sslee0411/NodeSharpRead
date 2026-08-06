using System.Runtime.InteropServices;

namespace NodeSharp.Runner.Diagnostics;

/// <summary>
/// Class명 : 크래시 덤프 수집기
/// 역활 및 기능 : 처리되지 않은 예외로 프로세스가 죽을 때 미니덤프 파일을 남기고 이벤트 로그에도 함께 기록하는 정적 클래스
///
/// (RN-06a) <see cref="Register"/>를 <c>Program.cs</c> 최상단에서 1회 호출해두면,
/// <c>AppDomain.CurrentDomain.UnhandledException</c>이 발생했을 때(Windows Service Watchdog가
/// 재기동만 시키고 사후 분석 단서를 남기지 않던 공백) <c>crashdumps/</c> 폴더에 덤프 파일을 쓰고
/// <see cref="EventLogWriter.WriteError"/>로 이벤트 로그에도 남깁니다. 최근 5개 덤프만 유지합니다
/// (덤프 파일은 수백MB로 클 수 있어 Historian 데이터와 별도 보존 개수 관리, 8번 탭 다세대 백업과
/// 동일 원칙). 설계 근거: 02번 문서 7번 탭 카드14.
/// </summary>
/// <remarks>
/// 완료 기준("프로세스를 강제 크래시시켰을 때 미니덤프 파일이 생성되는지 확인")의 실제 확인은
/// Win32 <c>Dbghelp.dll!MiniDumpWriteDump</c> P/Invoke에 의존해 Windows 전용이고, 이 개발
/// 환경(Linux 샌드박스)에서는 자동 검증이 불가능합니다 — <c>ClockDriftMonitor</c>(RN-05a)
/// 때와 동일한 유형이라 AskUserQuestion으로 확인, 같은 패턴을 적용했습니다. 그래서 이 클래스는
/// "덤프 파일 경로 명명 규칙"(<see cref="BuildDumpPath"/>)과 "최근 5개만 남기는 보존 정리"
/// (<see cref="EnforceRetention"/>), 그리고 두 로직이 실제 크래시 시 올바른 순서로 호출되는지
/// (<see cref="HandleCrash"/>)만 xUnit으로 자동 검증하고, 실제 Win32 덤프 생성·이벤트 로그 기록은
/// 사용자가 Windows에서 프로세스를 직접 강제 종료시켜 수동으로 확인합니다.
/// </remarks>
/// <example>
/// <code>
/// // Program.cs 최상단 — 앱이 뭘 하기도 전에 가장 먼저 등록해야 그 이후의 모든 예외를 잡을 수 있음
/// CrashDumpCollector.Register();
/// </code>
/// </example>
public static class CrashDumpCollector
{
    /// <summary>덤프 파일을 저장하는 하위 폴더 이름 — .gitignore(v1.23)에 이미 등록돼 있음.</summary>
    private const string DumpSubdirectory = "crashdumps";

    /// <summary>최근 몇 개의 덤프 파일까지만 남길지(그 이전 것은 오래된 순으로 삭제).</summary>
    private const int MaxRetainedDumps = 5;

    /// <summary>
    /// <c>AppDomain.CurrentDomain.UnhandledException</c>에 크래시 처리 핸들러를 등록합니다.
    /// <paramref name="baseDirectory"/>를 생략하면 실행 파일 폴더(<c>AppContext.BaseDirectory</c>)
    /// 기준으로 <c>crashdumps/</c> 폴더를 만듭니다. 이 메서드는 딱 한 번만 호출하면 됩니다
    /// (여러 번 호출하면 핸들러가 중복 등록되어 크래시 시 덤프도 여러 번 쓰입니다).
    /// </summary>
    public static void Register(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
            HandleCrash(directory, DateTime.UtcNow, WriteMiniDump, EventLogWriter.WriteError);
    }

    /// <summary>
    /// 크래시 1회를 처리합니다 — 덤프 경로를 만들고(<paramref name="writeMiniDump"/> 호출) 오래된
    /// 덤프를 정리한 뒤(<see cref="EnforceRetention"/>) 이벤트 로그에도 남깁니다
    /// (<paramref name="reportError"/> 호출). <paramref name="writeMiniDump"/>/<paramref name="reportError"/>를
    /// 주입받을 수 있게 해, 테스트에서는 실제 Win32/EventLog 호출 없이 이 메서드의 호출 순서·인자만
    /// 검증합니다(운영 코드는 <see cref="Register"/>가 기본값인 <see cref="WriteMiniDump"/>/
    /// <see cref="EventLogWriter.WriteError"/>를 그대로 넘깁니다).
    /// </summary>
    public static void HandleCrash(
        string baseDirectory,
        DateTime timestampUtc,
        Action<string> writeMiniDump,
        Action<string, string?> reportError)
    {
        var dumpDirectory = Path.Combine(baseDirectory, DumpSubdirectory);
        Directory.CreateDirectory(dumpDirectory);

        var path = BuildDumpPath(timestampUtc, baseDirectory);
        try
        {
            writeMiniDump(path);
        }
        catch
        {
            // 크래시 처리 핸들러 자체가 예외를 던지면 프로세스가 더 지저분하게 죽으므로 삼킨다.
            // (예: 이 환경에 Dbghelp.dll이 없는 비-Windows 실행 등) 덤프가 없어도 아래 이벤트 로그
            // 기록은 계속 시도한다.
        }

        EnforceRetention(dumpDirectory);
        reportError("NodeSharp.Runner가 예기치 않게 종료됨", path);
    }

    /// <summary>
    /// "<paramref name="baseDirectory"/>/crashdumps/runner_yyyyMMdd_HHmmss.dmp" 형식의 덤프 파일
    /// 경로를 만듭니다(02번 문서 7번 탭 카드14 그대로).
    /// </summary>
    public static string BuildDumpPath(DateTime timestampUtc, string baseDirectory) =>
        Path.Combine(baseDirectory, DumpSubdirectory, $"runner_{timestampUtc:yyyyMMdd_HHmmss}.dmp");

    /// <summary>
    /// <paramref name="dumpDirectory"/> 안의 <c>*.dmp</c> 파일 중 마지막 수정 시각이 가장 최근인
    /// <paramref name="maxRetained"/>개만 남기고 나머지를 삭제합니다. 폴더가 없으면 아무 일도
    /// 하지 않습니다.
    /// </summary>
    public static void EnforceRetention(string dumpDirectory, int maxRetained = MaxRetainedDumps)
    {
        if (!Directory.Exists(dumpDirectory))
        {
            return;
        }

        var oldDumps = new DirectoryInfo(dumpDirectory)
            .GetFiles("*.dmp")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(maxRetained);

        foreach (var file in oldDumps)
        {
            file.Delete();
        }
    }

    /// <summary>
    /// 현재 프로세스의 미니덤프를 <paramref name="path"/>에 씁니다 — Win32 <c>Dbghelp.dll</c>의
    /// <c>MiniDumpWriteDump</c>를 P/Invoke로 호출합니다(02번 문서 7번 탭 카드14가 제시한 두 방식
    /// 중 하나, 추가 NuGet 패키지 없이 쓸 수 있는 쪽을 선택). Windows가 아니면 DLL을 찾지 못해
    /// 예외가 나며, 이는 <see cref="HandleCrash"/>가 삼킵니다.
    /// </summary>
    private static void WriteMiniDump(string path)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        MiniDumpWriteDump(
            process.Handle,
            (uint)process.Id,
            fileStream.SafeFileHandle,
            MiniDumpType.WithFullMemory,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    /// <summary>Win32 MINIDUMP_TYPE 중 이 프로젝트가 쓰는 값만 옮겨 적음(전체 메모리 포함).</summary>
    private enum MiniDumpType : uint
    {
        WithFullMemory = 0x00000002
    }

    [DllImport("Dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeHandle hFile,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);
}
