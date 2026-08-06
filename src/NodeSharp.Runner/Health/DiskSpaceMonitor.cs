namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 디스크 여유 공간 모니터
/// 역활 및 기능 : flows.json/historian.db가 저장되는 드라이브의 여유 공간을 주기 확인해 Ok/Warning/Critical로 판정하는 클래스
///
/// (RN-05b-a) <c>DriveInfo</c>로 <paramref name="dataRoot"/>가 속한 드라이브의 여유 공간 비율(%)을
/// 읽어 20% 초과 Ok/10% 초과 Warning/그 이하 Critical로 분류합니다. Worker가 5분마다(카드12
/// ClockDriftMonitor와 같은 주기) <see cref="Check"/>를 호출해 RunnerHealthState에 결과를 기록하고,
/// /health 응답의 DiskSpace 필드로 노출합니다.
/// 설계 근거: 02번 문서 7번 탭 카드13.
/// </summary>
/// <remarks>
/// 원래 완료 기준은 "위험 단계에서 RetentionSweeper 즉시 강제 실행"까지 포함했지만, 설계 검토 중
/// RetentionSweeper가 ED-D10(Phase 13)에 속해 있고 이 Step 시점(Phase 4)에는 아직 만들어지지
/// 않았음을 발견 — AskUserQuestion으로 확인, "RN-05b-a(판정 로직)/RN-05b-b(RetentionSweeper 연동,
/// ED-D10 완료 후 착수)로 분리(추천)" 선택(RN-04→RN-04a/RN-04b, RN-01→RN-01a/RN-01b 분리와 동일한
/// 판단). 이 클래스는 <see cref="DiskSpaceLevel.Critical"/> 판정까지만 책임지고, 실제
/// RetentionSweeper 강제 실행 연동은 RN-05b-b에서 이어집니다.
///
/// 실제 OS I/O(<c>DriveInfo</c>)는 이 개발 환경(Linux 샌드박스)에서 Windows 드라이브 구조와 달라
/// 그대로 검증하기 어려워, <see cref="ClockDriftMonitor"/>의 <c>offsetReader</c> 주입과 동일한
/// 패턴으로 <paramref name="reader"/>를 주입받을 수 있게 설계했습니다 — 테스트는 가짜 reader로
/// 판정 로직만, 실제 운영 코드는 기본값인 <c>DriveInfo</c> 읽기를 씁니다.
/// </remarks>
/// <example>
/// <code>
/// // 운영 코드 — 실제 DriveInfo를 읽는 기본 동작
/// var monitor = new DiskSpaceMonitor(dataRoot: AppContext.BaseDirectory);
/// var status = monitor.Check();
///
/// // 테스트 코드 — 판정 로직만 검증(가짜 reader 주입)
/// var testMonitor = new DiskSpaceMonitor(dataRoot: "C:\\", reader: () =&gt; (500L, 5.0));
/// var result = testMonitor.Check();
/// Assert.Equal(DiskSpaceLevel.Critical, result.Level);
/// </code>
/// </example>
public sealed class DiskSpaceMonitor
{
    private readonly Func<(long AvailableFreeBytes, double FreePercent)> _reader;

    /// <summary>
    /// <paramref name="reader"/>를 주입받으면 그 함수로 (여유 바이트, 여유 비율%)을 얻고, 생략하면
    /// 기본값인 <paramref name="dataRoot"/> 드라이브의 실제 <c>DriveInfo</c> 값을 씁니다.
    /// </summary>
    public DiskSpaceMonitor(string dataRoot, Func<(long AvailableFreeBytes, double FreePercent)>? reader = null)
    {
        _reader = reader ?? (() => ReadFromDriveInfo(dataRoot));
    }

    /// <summary>
    /// 여유 공간을 1회 읽어 <see cref="DiskSpaceLevel"/>로 분류한 <see cref="DiskSpaceStatus"/>를
    /// 반환합니다. 여유 비율 20% 초과는 Ok, 10% 초과는 Warning, 그 이하는 Critical입니다.
    /// </summary>
    public DiskSpaceStatus Check()
    {
        var (availableFreeBytes, freePercent) = _reader();
        var level = freePercent switch
        {
            > 20 => DiskSpaceLevel.Ok,
            > 10 => DiskSpaceLevel.Warning,
            _ => DiskSpaceLevel.Critical
        };
        return new DiskSpaceStatus(availableFreeBytes, freePercent, level, DateTime.UtcNow);
    }

    /// <summary>
    /// <paramref name="dataRoot"/>가 속한 드라이브의 <c>DriveInfo</c>로 (여유 바이트, 여유 비율%)을
    /// 계산합니다.
    /// </summary>
    private static (long AvailableFreeBytes, double FreePercent) ReadFromDriveInfo(string dataRoot)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataRoot))!);
        var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
        return (drive.AvailableFreeSpace, freePercent);
    }
}
