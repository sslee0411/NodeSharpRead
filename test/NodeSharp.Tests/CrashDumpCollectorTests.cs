using NodeSharp.Runner.Diagnostics;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="CrashDumpCollector"/>(RN-06a)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-06a)은
/// "프로세스를 강제 크래시시켰을 때 미니덤프 파일이 생성되는지 확인"이지만, 실제 덤프 생성은 Win32
/// Dbghelp.dll P/Invoke에 의존해 Windows 전용이라 이 개발 환경(Linux 샌드박스)에서는 자동 검증이
/// 불가능합니다 — 그래서 이 테스트 파일은 실제 Win32 호출 없이 경로 명명 규칙(BuildDumpPath)·
/// 보존 정리(EnforceRetention)·크래시 처리 흐름(HandleCrash)만 가짜 delegate로 검증합니다. 실제
/// Win32 덤프 생성·이벤트 로그 기록은 사용자가 Windows에서 직접 확인합니다.
/// </summary>
public class CrashDumpCollectorTests
{
    [Fact]
    public void 완료_기준_직접_검증__BuildDumpPath는_baseDirectory_crashdumps_runner_타임스탬프_dmp_형식이다()
    {
        var timestamp = new DateTime(2026, 8, 6, 13, 5, 9, DateTimeKind.Utc);

        var path = CrashDumpCollector.BuildDumpPath(timestamp, "C:\\NodeSharp");

        Assert.Equal(Path.Combine("C:\\NodeSharp", "crashdumps", "runner_20260806_130509.dmp"), path);
    }

    [Fact]
    public void 완료_기준_직접_검증__EnforceRetention은_최근_5개만_남기고_나머지를_삭제한다()
    {
        var tempDir = Directory.CreateTempSubdirectory("crashdumptest_").FullName;
        try
        {
            // 오래된 것부터 새 것까지 7개 생성 — 파일명 순서와 상관없이 LastWriteTimeUtc 기준으로 판단되는지 확인
            var files = new List<string>();
            for (var i = 0; i < 7; i++)
            {
                var file = Path.Combine(tempDir, $"runner_{i}.dmp");
                File.WriteAllText(file, "dummy");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(i));   // i가 클수록 최신
                files.Add(file);
            }

            CrashDumpCollector.EnforceRetention(tempDir, maxRetained: 5);

            var remaining = Directory.GetFiles(tempDir, "*.dmp").Select(Path.GetFileName).ToHashSet();
            Assert.Equal(5, remaining.Count);
            // 가장 최근 5개(인덱스 2~6)만 남아야 함 — 인덱스 0/1(가장 오래된 것)은 삭제됨
            Assert.DoesNotContain("runner_0.dmp", remaining);
            Assert.DoesNotContain("runner_1.dmp", remaining);
            Assert.Contains("runner_6.dmp", remaining);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void 완료_기준_직접_검증__EnforceRetention은_폴더가_없으면_예외_없이_아무_일도_하지_않는다()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}");

        var ex = Record.Exception(() => CrashDumpCollector.EnforceRetention(missingDir));

        Assert.Null(ex);
    }

    [Fact]
    public void 완료_기준_직접_검증__HandleCrash는_writeMiniDump와_reportError를_올바른_인자로_호출한다()
    {
        var tempDir = Directory.CreateTempSubdirectory("crashdumptest_").FullName;
        try
        {
            var timestamp = new DateTime(2026, 8, 6, 9, 0, 0, DateTimeKind.Utc);
            string? capturedDumpPath = null;
            string? capturedMessage = null;
            string? capturedDetail = null;

            CrashDumpCollector.HandleCrash(
                tempDir,
                timestamp,
                writeMiniDump: path =>
                {
                    capturedDumpPath = path;
                    File.WriteAllText(path, "dummy-dump");   // 가짜 덤프 파일 생성(EnforceRetention 대상이 되는지도 함께 확인)
                },
                reportError: (message, detail) =>
                {
                    capturedMessage = message;
                    capturedDetail = detail;
                });

            var expectedPath = CrashDumpCollector.BuildDumpPath(timestamp, tempDir);
            Assert.Equal(expectedPath, capturedDumpPath);
            Assert.True(File.Exists(expectedPath));   // Directory.CreateDirectory가 미리 폴더를 만들어둔 덕분에 성공
            Assert.Equal("NodeSharp.Runner가 예기치 않게 종료됨", capturedMessage);
            Assert.Equal(expectedPath, capturedDetail);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void 완료_기준_직접_검증__HandleCrash는_writeMiniDump가_예외를_던져도_reportError는_호출한다()
    {
        var tempDir = Directory.CreateTempSubdirectory("crashdumptest_").FullName;
        try
        {
            var reportErrorCalled = false;

            CrashDumpCollector.HandleCrash(
                tempDir,
                DateTime.UtcNow,
                writeMiniDump: _ => throw new InvalidOperationException("이 환경에는 Dbghelp.dll이 없다고 가정(비-Windows)"),
                reportError: (_, _) => reportErrorCalled = true);

            Assert.True(reportErrorCalled);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
