using NodeSharp.Runner.Health;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ClockDriftMonitor"/>(RN-05a)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-05a)은
/// 실제 Windows에서 로컬 시각을 인위적으로 어긋나게 한 뒤 /health에 반영되는지 확인하는 것이라
/// 이 개발 환경(Linux 샌드박스, w32tm 자체가 없음)에서는 검증할 수 없습니다 — 사용자 확인을 거쳐
/// 이 테스트 파일은 오프셋 값을 가짜 reader로 주입해 Ok/Warning/Critical 판정 로직만 검증합니다.
/// 실제 w32tm 읽기는 사용자가 Windows에서 직접 확인합니다.
/// </summary>
public class ClockDriftMonitorTests
{
    [Theory]
    [InlineData(0.0, ClockDriftLevel.Ok)]
    [InlineData(0.9, ClockDriftLevel.Ok)]
    [InlineData(-0.9, ClockDriftLevel.Ok)]
    [InlineData(1.0, ClockDriftLevel.Warning)]
    [InlineData(3.0, ClockDriftLevel.Warning)]
    [InlineData(-4.9, ClockDriftLevel.Warning)]
    [InlineData(5.0, ClockDriftLevel.Critical)]
    [InlineData(10.0, ClockDriftLevel.Critical)]
    [InlineData(-100.0, ClockDriftLevel.Critical)]
    public async Task 완료_기준_직접_검증__오프셋_절대값에_따라_Ok_Warning_Critical로_분류된다(double offset, ClockDriftLevel expected)
    {
        var monitor = new ClockDriftMonitor(offsetReader: _ => Task.FromResult(offset));

        var status = await monitor.CheckAsync(CancellationToken.None);

        Assert.Equal(expected, status.Level);
        Assert.Equal(offset, status.OffsetSeconds);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__CheckedAt은_확인_시각_UTC로_채워진다()
    {
        var before = DateTime.UtcNow;
        var monitor = new ClockDriftMonitor(offsetReader: _ => Task.FromResult(0.0));

        var status = await monitor.CheckAsync(CancellationToken.None);

        var after = DateTime.UtcNow;
        Assert.InRange(status.CheckedAt, before, after);
    }
}
