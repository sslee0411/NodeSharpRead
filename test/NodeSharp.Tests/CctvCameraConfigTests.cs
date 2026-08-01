using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="CctvCameraConfig"/>/<see cref="RtspTransportMode"/>(CT-10, 02번 설계 문서 9번 탭 카드 17)에
/// 대한 단위 테스트입니다. 완료 기준: JSON 왕복 후에도 필드가 보존되고, CredentialRefId가 null이어도
/// (무인증 카메라) 정상 동작하는지 확인.
/// </summary>
public class CctvCameraConfigTests
{
    [Fact]
    public void CctvCameraConfig는_JSON_왕복_후에도_모든_필드가_보존된다()
    {
        var original = new CctvCameraConfig(
            Url: "rtsp://192.168.1.50:554/stream1",
            CredentialRefId: "cam01-cred",
            Transport: RtspTransportMode.Tcp,
            ReconnectIntervalSeconds: 10);

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<CctvCameraConfig>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void CctvCameraConfig는_CredentialRefId가_null이어도_무인증_카메라로_정상_동작한다()
    {
        var open = new CctvCameraConfig(
            Url: "rtsp://192.168.1.51:554/stream1",
            CredentialRefId: null,
            Transport: RtspTransportMode.Udp,
            ReconnectIntervalSeconds: 15);

        string json = JsonSerializer.Serialize(open);
        var restored = JsonSerializer.Deserialize<CctvCameraConfig>(json);

        Assert.Null(restored!.CredentialRefId);
        Assert.Equal(open, restored);
    }

    [Theory]
    [InlineData(RtspTransportMode.Tcp)]
    [InlineData(RtspTransportMode.Udp)]
    public void RtspTransportMode_두_값_모두_JSON_왕복이_보존된다(RtspTransportMode transport)
    {
        var config = new CctvCameraConfig("rtsp://cam/stream", null, transport, 10);

        string json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<CctvCameraConfig>(json);

        Assert.Equal(transport, restored!.Transport);
    }

    [Fact]
    public void RtspTransportMode는_Tcp_Udp_2종만_존재한다()
    {
        var values = Enum.GetValues<RtspTransportMode>();

        Assert.Equal(2, values.Length);
        Assert.Contains(RtspTransportMode.Tcp, values);
        Assert.Contains(RtspTransportMode.Udp, values);
    }
}
