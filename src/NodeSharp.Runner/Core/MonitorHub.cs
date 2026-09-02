using Microsoft.AspNetCore.SignalR;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Models;

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
/// <item><b>(LK-03) 인증은 이 Hub 앞단에서 끝남</b>: 02번 문서 7번 탭 카드6의 <c>RunnerAuthOptions</c>/
/// <c>TokenAuthMiddleware</c>가 <c>Program.cs</c>에서 <c>/hubs/monitor</c> 경로 전체(협상·연결·이
/// Hub의 모든 메서드 호출 포함)에 적용돼 있어, 이 클래스에 도달하는 시점엔 이미 유효한
/// <c>X-NodeSharp-Token</c>으로 인증이 끝난 상태입니다 — 이 Hub 자신은 인증을 전혀 신경 쓰지
/// 않습니다(<see cref="TokenAuthMiddleware"/> 클래스 문서 참고). 로컬호스트 전용 바인딩(RN-04a)은
/// 여전히 1차 방어선으로 유지됩니다.</item>
/// <item><b>(LK-02b 후속) 첫 클라이언트→서버 Hub 메서드 — <see cref="TriggerInject"/></b>: 사용자가
/// "Inject 노드를 클릭/버튼으로 트리거하는 기능이 안 보인다"고 보고해 조사한 결과, 지금까지 이 클래스가
/// 예고했던 대로 "Editor가 Runner에 무언가를 요청하는 흐름"이 실제로 하나도 없었습니다 — 이 메서드가
/// 그 첫 사례입니다. <see cref="CurrentEngineHolder"/>(신규, 이 폴더)를 생성자로 주입받아 "지금 배포된
/// 엔진"에 접근하고, <c>FlowEngine.TriggerManualAsync</c>로 위임만 합니다(어떤 노드 타입이 수동
/// 트리거를 지원하는지는 <c>IManuallyTriggerable</c>/<c>FlowEngine</c> 쪽 책임 — 이 Hub는 전혀 모릅니다).</item>
/// <item><b>(LK-03) <see cref="ReissueToken"/></b>: Editor "파일 → 토큰 재발급" 메뉴가 호출하는
/// 두 번째 클라이언트→서버 메서드입니다 — 자체 문서 참고.</item>
/// <item><b>(LK-04) <see cref="GetMsgTrace"/></b>: <c>NodeErrorEvent</c>를 받은 Editor가 "이 메시지가
/// 어디서 왔는지" 역추적하려고 호출하는 세 번째 클라이언트→서버 메서드입니다 — 자체 문서 참고.</item>
/// <item><b>(PD-01e) <see cref="SetSimulatedRegister"/></b>: Editor의 <c>SimulatorPanelView</c>가
/// 이제 <c>VirtualModbusSlave</c>를 직접 들고 있지 않고(Runner가 소유 — 클래스 문서의 "Runner로
/// 이전 + SignalR 원격제어" 설계 변경 참고) 이 네 번째 클라이언트→서버 메서드로 원격 기입만 합니다.</item>
/// </list>
/// </remarks>
public sealed class MonitorHub : Hub
{
    private readonly CurrentEngineHolder _engineHolder;
    private readonly RunnerTokenStore _tokenStore;
    private readonly MsgTraceStore _msgTraceStore;
    private readonly SimulationSlaveHolder _simulationSlaveHolder;

    /// <summary>
    /// DI가 <see cref="AddSingleton{TService}"/>로 등록된 <see cref="CurrentEngineHolder"/>/
    /// <see cref="RunnerTokenStore"/>/<see cref="MsgTraceStore"/>(LK-04)/<see cref="SimulationSlaveHolder"/>
    /// (PD-01e)를 자동으로 주입합니다.
    /// </summary>
    public MonitorHub(CurrentEngineHolder engineHolder, RunnerTokenStore tokenStore, MsgTraceStore msgTraceStore, SimulationSlaveHolder simulationSlaveHolder)
    {
        _engineHolder = engineHolder;
        _tokenStore = tokenStore;
        _msgTraceStore = msgTraceStore;
        _simulationSlaveHolder = simulationSlaveHolder;
    }

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

    /// <summary>
    /// (LK-03) Editor "파일 → 토큰 재발급" 메뉴가 이 메서드를 호출합니다. 호출 자체가 성공했다는
    /// 것은 이미 <see cref="TokenAuthMiddleware"/>를 통과했다는 뜻(=이 연결은 옛 토큰으로 정상
    /// 인증된 상태)이므로, 이 메서드 안에서 다시 권한을 확인하지 않습니다. <see cref="RunnerTokenStore.ReissueAsync"/>로
    /// 새 토큰을 생성·영속화한 뒤, <b>호출자를 제외한</b> 다른 모든 연결(<see cref="Hub.Clients"/>의
    /// <c>Others</c>)에 "tokenReissued" 메시지를 보내 스스로 연결을 끊고 재인증하도록 유도합니다
    /// (호출자 자신은 반환값으로 새 토큰을 이미 받으므로 별도 알림이 필요 없음 —
    /// <c>EditorMonitorClient.ReissueTokenAsync</c> 쪽 처리 참고). 완료 기준("재발급 시 기존 토큰
    /// 즉시 무효화")은 <see cref="RunnerTokenStore"/>가 값을 교체하는 즉시 만족되며, 이 브로드캐스트는
    /// 이미 연결된 다른 클라이언트까지 능동적으로 끊어주는 추가 보강입니다. <see cref="Hub.Context"/>
    /// 대신 <see cref="AppContext.BaseDirectory"/>를 직접 씁니다 — <c>Worker.ExecuteAsync</c>/
    /// <c>StartupSequencer</c>와 동일하게 Runner의 데이터 폴더는 항상 실행 파일 폴더 기준입니다.
    /// </summary>
    /// <returns>새로 발급된 토큰 값 — 호출자(Editor)가 자신의 인증 헤더를 즉시 갱신하는 데 씁니다.</returns>
    public async Task<string> ReissueToken()
    {
        var newToken = await _tokenStore.ReissueAsync(AppContext.BaseDirectory, CancellationToken.None);
        await Clients.Others.SendAsync("tokenReissued");
        return newToken;
    }

    /// <summary>
    /// (LK-04) Editor가 <c>NodeErrorEvent</c>를 받은 뒤(또는 사용자가 직접 요청했을 때) 그 이벤트의
    /// <c>MsgId</c>로 이 메서드를 호출해, 해당 메시지가 지금까지 거쳐온 전체 경로(<see cref="MsgTrace"/>)를
    /// 요청합니다. <see cref="MsgTraceStore.GetTrace"/>에 그대로 위임만 합니다 — 이 Hub는 Trace를
    /// 어떻게 누적하는지 전혀 모릅니다(<see cref="TriggerInject"/>가 <see cref="CurrentEngineHolder"/>에
    /// 위임만 하는 것과 동일한 얇은 창구 원칙). 한 번도 추적된 적이 없거나(예: msg.Id 오타) 상한을
    /// 넘어 이미 제거됐으면 <c>null</c>을 반환합니다 — 이 Hub는 오류를 구분해 알리지 않습니다(완료
    /// 기준 범위 밖, <see cref="TriggerInject"/>와 동일한 원칙).
    /// </summary>
    /// <param name="msgId">추적할 메시지의 고유 식별자(<c>Msg.Id</c>, <c>NodeErrorEvent.MsgId</c>와 동일).</param>
    /// <returns>메시지가 거쳐온 경로, 또는 추적된 적 없으면 <c>null</c>.</returns>
    public Task<MsgTrace?> GetMsgTrace(string msgId) => Task.FromResult(_msgTraceStore.GetTrace(msgId));

    /// <summary>
    /// (PD-01e) Editor의 <c>SimulatorPanelView</c>가 사용자가 표에서 값을 편집할 때마다 호출합니다.
    /// <paramref name="plcId"/>(<c>PlcNode.Id</c>)에 대응하는 <c>VirtualModbusSlave</c>가
    /// <see cref="SimulationSlaveHolder"/>에 등록돼 있으면(시뮬레이션 모드 PLC로 device.json에 저장돼
    /// 있고, <c>SimulationDeviceBinder</c>가 이미 배선을 마쳤으면) 그 레지스터에 값을 씁니다 — 다음
    /// <c>DeviceMapPoller</c> 폴링 주기에 <see cref="TagValueUpdatedEvent"/>로 자동 중계됩니다(별도
    /// 알림을 이 메서드가 직접 보내지 않음, <c>DeviceMapPoller.PollOnceAsync</c>의 "값이 실제로
    /// 바뀐 태그마다 발행" 규약 그대로). 등록돼 있지 않으면(구조 재로드 경합, 잘못된 plcId 등)
    /// <see cref="TriggerInject"/>·<see cref="GetMsgTrace"/>와 동일한 원칙으로 조용히 무시합니다.
    /// </summary>
    /// <param name="plcId">값을 쓸 대상 PLC(PlcNode.Id).</param>
    /// <param name="address">Modbus 레지스터 주소(0~65535).</param>
    /// <param name="value">쓸 값(0~65535) — <see cref="ushort"/> 범위를 벗어나면 조용히 무시합니다.</param>
    public Task SetSimulatedRegister(string plcId, int address, int value)
    {
        if (address is < 0 or > 65535 || value is < 0 or > 65535)
        {
            return Task.CompletedTask;
        }

        _simulationSlaveHolder.TryGet(plcId)?.SetRegister(address, (ushort)value);
        return Task.CompletedTask;
    }
}
