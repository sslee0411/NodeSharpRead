namespace NodeSharp.Contracts.Enums;

/// <summary>
/// RTSP 세션의 하위 전송 프로토콜입니다. <see cref="Models.CctvCameraConfig.Transport"/>에 사용되며,
/// WPF <c>CctvViewerControl</c>(LibVLCSharp)이 <c>Media.AddOption</c>에 <c>:rtsp-tcp</c>/<c>:rtsp-udp</c>
/// 옵션을 넘길 때 이 값을 참조합니다.
/// 설계 근거: 02번 문서 9번 탭 카드 17(v1.62 신설 — 사용자 요청 "RTSP로 카메라에 접속해 화면 영상을
/// 보는 기능").
/// </summary>
/// <example>
/// <code>
/// var config = new CctvCameraConfig(
///     Url: "rtsp://192.168.1.50:554/stream1",
///     CredentialRefId: "cam01-cred",
///     Transport: RtspTransportMode.Tcp,   // 대부분의 IP 카메라는 Tcp가 더 안정적(패킷 손실 시 프레임 깨짐 방지)
///     ReconnectIntervalSeconds: 10);
/// </code>
/// </example>
public enum RtspTransportMode
{
    /// <summary>RTSP interleaved TCP. 방화벽/NAT 환경에서 UDP보다 안정적이라 기본 권장값.</summary>
    Tcp,

    /// <summary>RTP over UDP. 지연은 더 낮지만 네트워크 상황에 따라 프레임 손실이 발생할 수 있음.</summary>
    Udp
}
