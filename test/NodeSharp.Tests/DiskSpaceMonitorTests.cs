using NodeSharp.Runner.Health;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="DiskSpaceMonitor"/>(RN-05b-a)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-05b-a)은
/// 여유 공간 비율(%)이 20% 초과 Ok/10% 초과 Warning/그 이하 Critical로 정확히 분류되는지 확인하는
/// 것이라, 이 개발 환경(Linux 샌드박스)의 실제 드라이브 크기에 좌우되지 않도록 가짜 reader로
/// 여유 비율 값을 직접 주입해 판정 로직만 검증합니다. 실제 DriveInfo 읽기는 운영 코드 기본
/// 경로(<see cref="DiskSpaceMonitor"/> 생성자에서 reader를 생략했을 때)로, 이 테스트 파일에서는
/// 다루지 않습니다.
/// </summary>
public class DiskSpaceMonitorTests
{
    [Theory]
    [InlineData(100.0, DiskSpaceLevel.Ok)]
    [InlineData(20.1, DiskSpaceLevel.Ok)]
    [InlineData(20.0, DiskSpaceLevel.Warning)]
    [InlineData(15.0, DiskSpaceLevel.Warning)]
    [InlineData(10.1, DiskSpaceLevel.Warning)]
    [InlineData(10.0, DiskSpaceLevel.Critical)]
    [InlineData(5.0, DiskSpaceLevel.Critical)]
    [InlineData(0.0, DiskSpaceLevel.Critical)]
    public void 완료_기준_직접_검증__여유_비율에_따라_Ok_Warning_Critical로_분류된다(double freePercent, DiskSpaceLevel expected)
    {
        var monitor = new DiskSpaceMonitor(dataRoot: ".", reader: () => (1000L, freePercent));

        var status = monitor.Check();

        Assert.Equal(expected, status.Level);
        Assert.Equal(freePercent, status.FreePercent);
    }

    [Fact]
    public void 완료_기준_직접_검증__AvailableFreeBytes와_CheckedAt이_reader_결과와_확인_시각으로_채워진다()
    {
        var before = DateTime.UtcNow;
        var monitor = new DiskSpaceMonitor(dataRoot: ".", reader: () => (123_456L, 50.0));

        var status = monitor.Check();

        var after = DateTime.UtcNow;
        Assert.Equal(123_456L, status.AvailableFreeBytes);
        Assert.InRange(status.CheckedAt, before, after);
    }
}
