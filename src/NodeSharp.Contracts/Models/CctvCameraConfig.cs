using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// <c>CctvViewerNode</c>(Runner, DB-07)가 <c>flows.json</c>에 저장하는 RTSP 카메라 연결 설정입니다.
/// 이 레코드는 "연결 정보"만 담고 실제 영상 세션은 열지 않습니다 — WPF <c>CctvViewerControl</c>이
/// 이 값을 읽어 자체 RTSP 세션을 별도로 엽니다(9번 탭 카드17 "연결이 2개로 분리된다" 설계 참고).
/// 설계 근거: 02번 문서 9번 탭 카드 17(v1.62 신설 — 사용자 요청 "RTSP로 카메라에 접속해 화면 영상을
/// 보는 기능"). DB-07(CctvViewerNode+WPF 위젯)이 이 모델에 의존하므로 CT-10에서 먼저 정의한다.
/// </summary>
/// <param name="Url">자격증명을 제외한 RTSP 접속 주소(예: <c>rtsp://192.168.1.50:554/stream1</c>). 사용자명/비밀번호는
/// 절대 이 문자열에 포함하지 않는다 — <see cref="Interfaces.ICredentialStore"/>(CT-04c)를 <see cref="CredentialRefId"/>로만
/// 참조하고, 실제 접속 URL 조합은 <c>CctvViewerControl</c>이 메모리에서만 수행한다(카드 2의 자격증명 원칙과 동일).</param>
/// <param name="CredentialRefId">인증이 필요한 카메라의 자격증명 참조 Id. 무인증 카메라는 <c>null</c> — 이 경우
/// <c>CctvViewerControl</c>은 사용자명/비밀번호 조합 없이 <see cref="Url"/>을 그대로 사용한다.</param>
/// <param name="Transport">RTSP 하위 전송 프로토콜(<see cref="RtspTransportMode"/>).</param>
/// <param name="ReconnectIntervalSeconds"><c>CctvViewerNode</c>가 host:port 도달성을 재확인하는 주기(초).
/// 실제 영상 재연결과는 무관 — 캔버스 상태 점(초록/빨강) 갱신 주기일 뿐이다.</param>
/// <example>
/// <code>
/// // 인증이 필요한 카메라
/// var authed = new CctvCameraConfig(
///     Url: "rtsp://192.168.1.50:554/stream1",
///     CredentialRefId: "cam01-cred",
///     Transport: RtspTransportMode.Tcp,
///     ReconnectIntervalSeconds: 10);
///
/// // 무인증 카메라(CredentialRefId 생략)
/// var open = new CctvCameraConfig(
///     Url: "rtsp://192.168.1.51:554/stream1",
///     CredentialRefId: null,
///     Transport: RtspTransportMode.Udp,
///     ReconnectIntervalSeconds: 10);
///
/// // JSON 왕복 후에도 모든 필드가 그대로 보존되어야 한다(완료 기준)
/// string json = System.Text.Json.JsonSerializer.Serialize(authed);
/// var restored = System.Text.Json.JsonSerializer.Deserialize&lt;CctvCameraConfig&gt;(json);
/// </code>
/// </example>
public sealed record CctvCameraConfig(
    string Url,
    string? CredentialRefId,
    RtspTransportMode Transport,
    int ReconnectIntervalSeconds);
