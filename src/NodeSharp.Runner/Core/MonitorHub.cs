using Microsoft.AspNetCore.SignalR;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 모니터링 Hub
/// 역활 및 기능 : Runner→Editor 실시간 모니터링 이벤트를 중계하는 순수 서버→클라이언트 push용 SignalR Hub
///
/// (LK-02a) 02번 설계 문서 7번 탭 카드2(<c>MonitorHub</c>)가 지정한 그대로 — 이 Hub 자신은 클라이언트가
/// 호출할 메서드를 갖지 않습니다(1차 범위). 실제 이벤트 전송은 <see cref="StatusBroadcaster"/>가
/// <see cref="IHubContext{THub}"/>를 통해 <c>Clients.All.SendAsync(...)</c>로 수행하므로, 이 클래스는
/// SignalR이 클라이언트 연결(<c>HubConnection</c>)을 식별·유지하기 위한 "빈 껍데기" 역할만 합니다
/// (<c>Program.cs</c>의 <c>app.MapHub&lt;MonitorHub&gt;(...)</c>가 실제 엔드포인트로 노출).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>인증 없음(LK-03 몫)</b>: 02번 문서 7번 탭 카드6의 <c>RunnerAuthOptions</c>/<c>TokenAuthMiddleware</c>는
/// 이 Step 범위가 아닙니다 — Step맵 <c>LK-03</c>(Editor↔Runner 토큰 인증)이 별도로 담당합니다. 지금은
/// 로컬호스트 전용 바인딩(<c>Program.cs</c>의 <c>UseUrls("http://localhost:47500")</c>, RN-04a)만으로
/// 최소한의 보호를 받습니다.</item>
/// <item><b>Hub 메서드가 왜 없는가</b>: Node-RED의 <c>comms.js</c>와 동일하게, 지금 필요한 통신은
/// "Runner가 Editor에게 이벤트를 알림(push)" 한 방향뿐입니다. Editor가 Runner에 무언가를 요청하는
/// 흐름(예: 수동 재배포 트리거)이 생기면 이 클래스에 <c>public</c> 메서드를 추가해 확장합니다.</item>
/// </list>
/// </remarks>
public sealed class MonitorHub : Hub
{
}
