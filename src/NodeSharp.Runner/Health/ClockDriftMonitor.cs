using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NodeSharp.Runner.Health;

/// <summary>
/// Class명 : 클럭 드리프트 모니터
/// 역활 및 기능 : Windows 내장 시각 동기화(W32Time) 상태를 주기 확인해 로컬 시계가 NTP 기준 대비 얼마나 벗어났는지 측정하는 클래스
///
/// (RN-05a) 자체 NTP 클라이언트를 구현하지 않고, <c>w32tm /query /status</c> 출력을 파싱해
/// Windows가 이미 계산해둔 오프셋 값을 그대로 읽습니다(시각 동기화 자체는 OS 책임, 이 클래스는
/// "동기화가 깨졌는지"만 감시). Worker가 5분마다 <see cref="CheckAsync"/>를 호출해
/// RunnerHealthState에 결과를 기록하고, /health 응답의 ClockDrift 필드로 노출합니다.
/// 설계 근거: 02번 문서 7번 탭 카드12.
/// </summary>
/// <remarks>
/// 오프셋을 실제로 읽는 <see cref="ReadOffsetFromW32TimeAsync"/>는 Windows 전용이고(Process로
/// <c>w32tm.exe</c> 실행), 이 개발 환경(Linux 샌드박스)에는 <c>w32tm</c> 자체가 없어 직접 실행
/// 검증이 불가능합니다 — 사용자 확인을 거쳐 판정 로직(Ok/Warning/Critical 분류)만 xUnit으로
/// 검증하고, 실제 오프셋 읽기는 사용자가 Windows에서 로컬 시각을 인위적으로 어긋나게 한 뒤
/// /health 응답에 반영되는지 직접 확인하기로 했습니다. 이를 위해 생성자가 <c>offsetReader</c>를
/// 주입받을 수 있게 설계했습니다(<c>NodeTypeRegistry.LoadPlugins</c>의 <c>PluginLoader</c> 주입과
/// 동일한 패턴) — 테스트는 가짜 reader로 판정 로직만, 실제 운영 코드는 기본값인 w32tm 파싱을 씁니다.
/// <c>w32tm /query /status</c>의 정확한 출력 형식(특히 "Phase Offset" 줄 존재 여부)은 Windows
/// 버전에 따라 다를 수 있어, 파싱이 예상과 다르면 <see cref="InvalidOperationException"/>을 던져
/// 원인을 바로 알 수 있게 했습니다 — 이 예외는 Worker의 주기 루프에서 잡아 다음 주기에 다시
/// 시도합니다(한 번 실패해도 전체 루프가 멈추지 않음).
/// </remarks>
/// <example>
/// <code>
/// // 운영 코드 — 실제 w32tm을 읽는 기본 동작
/// var monitor = new ClockDriftMonitor();
/// var status = await monitor.CheckAsync(ct);
///
/// // 테스트 코드 — 판정 로직만 검증(가짜 reader 주입)
/// var testMonitor = new ClockDriftMonitor(offsetReader: (_) =&gt; Task.FromResult(6.0));
/// var result = await testMonitor.CheckAsync(CancellationToken.None);
/// Assert.Equal(ClockDriftLevel.Critical, result.Level);
/// </code>
/// </example>
public sealed class ClockDriftMonitor
{
    private static readonly Regex PhaseOffsetPattern = new(@"Phase Offset:\s*(-?[0-9.]+)s", RegexOptions.Compiled);

    private readonly Func<CancellationToken, Task<double>> _offsetReader;

    /// <summary>
    /// <paramref name="offsetReader"/>를 주입받으면 그 함수로 오프셋(초)을 얻고, 생략하면
    /// 기본값인 <see cref="ReadOffsetFromW32TimeAsync"/>(실제 <c>w32tm.exe</c> 파싱)를 씁니다.
    /// </summary>
    public ClockDriftMonitor(Func<CancellationToken, Task<double>>? offsetReader = null)
    {
        _offsetReader = offsetReader ?? ReadOffsetFromW32TimeAsync;
    }

    /// <summary>
    /// 오프셋을 1회 읽어 <see cref="ClockDriftLevel"/>로 분류한 <see cref="ClockDriftStatus"/>를
    /// 반환합니다. 절대값 1초 미만은 Ok, 5초 미만은 Warning, 그 이상은 Critical입니다.
    /// </summary>
    public async Task<ClockDriftStatus> CheckAsync(CancellationToken ct)
    {
        var offset = await _offsetReader(ct);
        var level = Math.Abs(offset) switch
        {
            < 1.0 => ClockDriftLevel.Ok,
            < 5.0 => ClockDriftLevel.Warning,
            _ => ClockDriftLevel.Critical
        };
        return new ClockDriftStatus(offset, level, DateTime.UtcNow);
    }

    /// <summary>
    /// <c>w32tm /query /status</c>를 실행해 "Phase Offset: N.NNNNNNNs" 줄을 찾아 초 단위 값으로
    /// 반환합니다. 프로세스 실행에 실패하거나 그 줄을 찾지 못하면 원인을 담은
    /// <see cref="InvalidOperationException"/>을 던집니다(Worker의 주기 루프에서 격리 처리).
    /// </summary>
    private static async Task<double> ReadOffsetFromW32TimeAsync(CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "w32tm",
                Arguments = "/query /status",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var match = PhaseOffsetPattern.Match(output);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                "w32tm /query /status 출력에서 'Phase Offset' 줄을 찾지 못했습니다 — Windows 버전에 따라 출력 형식이 다를 수 있습니다.");
        }

        return double.Parse(match.Groups[1].Value);
    }
}
