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
/// <item><b>(LK-02b 후속) 첫 클라이언트→서버 Hub 메서드 — <see cref="TriggerInject"/></b>: 사용자가
/// "Inject 노드를 클릭/버튼으로 트리거하는 기능이 안 보인다"고 보고해 조사한 결과, 지금까지 이 클래스가
/// 예고했던 대로 "Editor가 Runner에 무언가를 요청하는 흐름"이 실제로 하나도 없었습니다 — 이 메서드가
/// 그 첫 사례입니다. <see cref="CurrentEngineHolder"/>(신규, 이 폴더)를 생성자로 주입받아 "지금 배포된
/// 엔진"에 접근하고, <c>FlowEngine.TriggerManualAsync</c>로 위임만 합니다(어떤 노드 타입이 수동
/// 트리거를 지원하는지는 <c>IManuallyTriggerable</c>/<c>FlowEngine</c> 쪽 책임 — 이 Hub는 전혀 모릅니다).</item>
/// </list>
/// </remarks>
public sealed class MonitorHub : Hub
{
    private readonly CurrentEngineHolder _engineHolder;

    /// <summary>DI가 <see cref="AddSingleton{TService}"/>로 등록된 <see cref="CurrentEngineHolder"/>를 자동으로 주입합니다.</summary>
    public MonitorHub(CurrentEngineHolder engineHolder) => _engineHolder = engineHolder;

    /// <summary>
    /// (LK-02b 후속) Editor 캔버스에서 Inject(또는 향후 다른 <c>IManuallyTriggerable</c> 구현) 노드의
    /// 트리거 버튼을 클릭하면 <c>EditorMonitorClient.TriggerInjectAsync</c>가 이 메서드를 호출합니다.
    /// 아직 한 번도 배포되지 않았거나(<c>_engineHolder.Engine</c>이 <c>null</c>) <paramref name="nodeId"/>가
    /// 배포에 없거나 수동 트리거를 지원하지 않으면 <c>FlowEngine.TriggerManualAsync</c>가 조용히
    /// 아무 일도 하지 않습니다(그 메서드 자체 문서 참고) — 이 Hub는 오류를 구분해 알리지 않습니다
    /// (완료 기준 범위 밖, 필요해지면 향후 확장). <see cref="Hub.Context"/>(<c>ConnectionAborted</c>)
    /// 대신 <see cref="CancellationToken.None"/>을 씁니다 — <c>Context</c>는 실제 SignalR 파이프라인을
    /// 거쳐야만 채워지는 프로퍼티라, 이 메서드를 직접 생성한 <see cref="MonitorHub"/> 인스턴스로
    /// xUnit에서 단위 테스트하려면(SignalR 커넥션 없이) 의존하지 않는 편이 낫다고 판단했습니다.
    /// </summary>
    public Task TriggerInject(string nodeId)
    {
        var engine = _engineHolder.Engine;
        return engine?.TriggerManualAsync(nodeId, payload: null, CancellationToken.None) ?? Task.CompletedTask;
    }
}
