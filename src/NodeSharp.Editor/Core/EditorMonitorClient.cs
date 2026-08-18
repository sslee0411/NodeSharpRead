using Microsoft.AspNetCore.SignalR.Client;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Editor.Core;

/// <summary>
/// Class명 : Editor 모니터링 클라이언트
/// 역활 및 기능 : Runner의 SignalR Hub("/hubs/monitor", LK-02a MonitorHub)에 접속해 4가지 모니터링
/// 이벤트(NodeStatusEvent/FlowActivityEvent/DebugMessageEvent/NodeErrorEvent)를 수신하고, 연결 상태를
/// bool 이벤트 하나로 단순화해 재발행하는 얇은 창구 클래스
///
/// (LK-02b) 02번 설계 문서 7번 탭 카드3 "EditorMonitorClient"가 그대로 — <see cref="StatusBroadcaster"/>
/// (Runner, LK-02a)가 <c>Clients.All.SendAsync("nodeStatus"/"flowActivity"/"debugMessage"/"nodeError", ...)</c>로
/// 보내는 4개 메서드명을 <see cref="HubConnection.On{T}(string, Action{T})"/>으로 그대로 받아 이 클래스
/// 자신의 C# 이벤트로 재발행합니다 — <c>FlowCanvasView</c>/<c>DebugSidebarView</c>(LK-02b 나머지 작업)는
/// SignalR 타입(<see cref="HubConnection"/> 등)을 전혀 몰라도 됩니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>고정 URL</b>: 기본값 <c>http://localhost:47500/hubs/monitor</c> — Runner의
/// <c>Program.cs</c>가 <c>UseUrls("http://localhost:47500")</c>로 로컬호스트 전용 바인딩하고(RN-04a),
/// <c>/hubs/monitor</c> 경로로 <c>MonitorHub</c>를 노출한 것(LK-02a)과 정확히 일치. 생성자 매개변수로
/// 덮어쓸 수 있게 해 향후 원격 배포 시나리오(별도 Phase)에도 대비.</item>
/// <item><b>자동 재연결</b>: <c>WithAutomaticReconnect()</c> 채택 — Runner 재시작(LK-01 재배포 등)
/// 중 Editor가 잠깐 연결을 잃어도 사용자가 수동으로 재접속할 필요가 없도록 함. 재연결 시도/성공/실패
/// 각 단계를 <see cref="ConnectionStateChanged"/> 하나로 뭉뚱그려 알림(연결됨=true, 그 외 전부 false) —
/// MainWindow의 연결 상태 배지(LK-02b 나머지 작업)가 세부 상태 전이를 몰라도 "연결됨/연결 안됨" 배지
/// 하나만 그리면 되도록 단순화(<c>FlowStore</c>/<c>JsonWriteService</c>와 같은 "얇은 창구" 관례).</item>
/// <item><b>구독 해제</b>: <see cref="IAsyncDisposable"/> 구현 — <c>StopAsync</c> 후
/// <c>DisposeAsync</c>까지 호출해 내부 <see cref="HubConnection"/>을 완전히 정리(<c>IEventBus</c> XML
/// 문서의 "구독은 반드시 해제" 원칙과 동일한 정신을 SignalR 연결 레벨에도 적용).</item>
/// <item><b>실패 격리</b>: 연결 시도 자체가 실패해도(Runner가 아직 안 켜져 있는 경우 등)
/// <see cref="StartAsync"/>가 예외를 던지는 대신 <see cref="ConnectionStateChanged"/>(false)만 알리고
/// 조용히 반환 — Editor가 Runner 없이도(오프라인 편집) 정상적으로 동작해야 하므로, 연결 실패가 Editor
/// 시작 자체를 막으면 안 됨(<c>FlowFileWatcher</c>의 "콜백 예외 격리"와 동일한 원칙).</item>
/// <item><b>(LK-02b 후속, 사용자 요청) <see cref="TriggerInjectAsync"/> — 첫 Editor→Runner 채널</b>:
/// 지금까지 이 클래스는 Runner→Editor 한 방향(모니터링 push)만 다뤘는데, 사용자가 "Inject 노드를
/// 클릭/버튼으로 트리거하는 방법을 모르겠다"고 보고해 조사한 결과 그 반대 방향 채널이 전혀 없었음을
/// 확인했습니다. <c>HubConnection.InvokeAsync</c>로 <c>MonitorHub.TriggerInject</c>(신규)를 호출하는
/// 첫 사례이며, 이 채널이 열리면서 이 클래스가 처음으로 "요청-응답"(비록 반환값은 없지만) 방향의
/// SignalR 호출도 겸하게 됐습니다.</item>
/// <item><b>(LK-03) 인증 헤더 — <see cref="SetToken"/>/<see cref="ReissueTokenAsync"/></b>: 생성자가
/// 받은(또는 <see cref="SetToken"/>으로 나중에 바꾼) 토큰 값을 <c>X-NodeSharp-Token</c> 헤더로
/// 실어 Runner의 <c>TokenAuthMiddleware</c>를 통과합니다. <c>HubConnectionBuilder.WithUrl</c>의
/// 설정 델리게이트는 <c>Build()</c> 시점에 한 번만 실행되지만, 그 델리게이트가 <c>opts.Headers</c>를
/// 이 클래스가 계속 들고 있는 <see cref="_headers"/> 딕셔너리 "그 자체"로 지정해뒀기 때문에(참조
/// 공유), <see cref="SetToken"/>으로 내용만 바꿔도 다음 연결 시도부터 새 값이 반영됩니다(
/// <c>HubConnection</c>을 통째로 다시 만들 필요 없음). <see cref="ReissueTokenAsync"/>는
/// <c>MonitorHub.ReissueToken</c>을 호출해 새 토큰을 받고 곧바로 <see cref="SetToken"/>까지
/// 이어서 호출합니다.</item>
/// <item><b>(LK-03) <see cref="TokenInvalidatedByServer"/></b>: Runner의 <c>MonitorHub.ReissueToken</c>이
/// (다른 연결에서 트리거돼) <c>Clients.Others</c>로 "tokenReissued"를 보내면 이 이벤트로 재발행합니다
/// — 이 연결이 그 "다른 연결"이라면 옛 토큰으로는 곧 재연결이 거부될 것이므로, 구독자(<c>MainWindow</c>)가
/// 스스로 연결을 끊고 사용자에게 새 토큰 재입력을 안내해야 합니다.</item>
/// <item><b>(LK-04) <see cref="GetMsgTraceAsync"/></b>: <see cref="NodeErrorReceived"/>로 에러를 받은
/// 호출부(<c>MainWindow</c>)가 그 이벤트의 <c>MsgId</c>로 Runner의 <c>MonitorHub.GetMsgTrace</c>를
/// 호출해 "이 메시지가 어디서 왔는지" 경로를 받아올 때 씁니다. <see cref="TriggerInjectAsync"/>와
/// 동일하게 연결돼 있지 않으면 예외를 던집니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var client = new EditorMonitorClient();
/// client.NodeStatusReceived += e => Dispatcher.Invoke(() => canvas.ApplyNodeStatus(e));
/// client.ConnectionStateChanged += connected => Dispatcher.Invoke(() => badge.SetConnected(connected));
/// await client.StartAsync();
/// // ... 앱 종료 시
/// await client.DisposeAsync();
/// </code>
/// </example>
public sealed class EditorMonitorClient : IAsyncDisposable
{
    private const string DefaultUrl = "http://localhost:47500/hubs/monitor";
    private const string TokenHeaderName = "X-NodeSharp-Token";

    // (LK-03) HubConnectionBuilder.WithUrl의 설정 델리게이트에 opts.Headers = _headers로 이 딕셔너리
    // "그 자체"를 넘겨두면, Build() 이후 이 딕셔너리 내용만 바꿔도(SetToken) 다음 연결 시도부터
    // 새 값이 반영된다 — 위 클래스 remarks "인증 헤더" 항목 참고.
    private readonly Dictionary<string, string> _headers = new();

    private readonly HubConnection _connection;

    /// <summary>연결됨 여부(<see cref="HubConnectionState.Connected"/>).</summary>
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    /// <summary>연결 상태가 바뀔 때마다 발생(연결됨=true, 그 외 전부 false) — SignalR 세부 상태 전이를 몰라도 되게 단순화.</summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>Runner가 "nodeStatus"로 보낸 <see cref="NodeStatusEvent"/> 수신 시 발생.</summary>
    public event Action<NodeStatusEvent>? NodeStatusReceived;

    /// <summary>Runner가 "flowActivity"로 보낸 <see cref="FlowActivityEvent"/> 수신 시 발생.</summary>
    public event Action<FlowActivityEvent>? FlowActivityReceived;

    /// <summary>Runner가 "debugMessage"로 보낸 <see cref="DebugMessageEvent"/> 수신 시 발생.</summary>
    public event Action<DebugMessageEvent>? DebugMessageReceived;

    /// <summary>Runner가 "nodeError"로 보낸 <see cref="NodeErrorEvent"/> 수신 시 발생.</summary>
    public event Action<NodeErrorEvent>? NodeErrorReceived;

    /// <summary>
    /// (LK-03) Runner의 <c>MonitorHub.ReissueToken</c>이 <c>Clients.Others</c>로 "tokenReissued"를
    /// 보내면 발생 — 위 클래스 remarks "TokenInvalidatedByServer" 항목 참고.
    /// </summary>
    public event Action? TokenInvalidatedByServer;

    /// <summary>
    /// <paramref name="runnerUrl"/>(기본 <c>http://localhost:47500/hubs/monitor</c>)로 연결을
    /// 준비합니다 — 이 시점엔 실제 네트워크 연결을 시도하지 않고, <see cref="StartAsync"/> 호출 시에만
    /// 시도합니다. (LK-03) <paramref name="token"/>이 있으면 <see cref="SetToken"/>으로 미리 인증
    /// 헤더를 채워둡니다(없으면 생성 이후 <see cref="SetToken"/>을 따로 호출해야 함 — 예: Runner의
    /// runner.token을 비동기로 읽어야 해 생성자 시점엔 아직 모를 때).
    /// </summary>
    public EditorMonitorClient(string runnerUrl = DefaultUrl, string? token = null)
    {
        SetToken(token);

        _connection = new HubConnectionBuilder()
            .WithUrl(runnerUrl, opts => opts.Headers = _headers)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<NodeStatusEvent>("nodeStatus", e => NodeStatusReceived?.Invoke(e));
        _connection.On<FlowActivityEvent>("flowActivity", e => FlowActivityReceived?.Invoke(e));
        _connection.On<DebugMessageEvent>("debugMessage", e => DebugMessageReceived?.Invoke(e));
        _connection.On<NodeErrorEvent>("nodeError", e => NodeErrorReceived?.Invoke(e));
        // (LK-03) Runner의 MonitorHub.ReissueToken이 "호출자를 제외한 다른 연결"에 보내는 알림 —
        // 이 연결이 그 "다른 연결"이라면 옛 토큰으로는 곧 재연결이 거부되므로 스스로 끊고 사용자에게
        // 알려야 한다(구독·처리는 호출부 MainWindow 책임, 이 클래스는 재발행만 함).
        _connection.On("tokenReissued", () => TokenInvalidatedByServer?.Invoke());

        _connection.Closed += _ => { ConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };
        _connection.Reconnecting += _ => { ConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };
        _connection.Reconnected += _ => { ConnectionStateChanged?.Invoke(true); return Task.CompletedTask; };
    }

    /// <summary>
    /// (LK-03) 인증 헤더(<c>X-NodeSharp-Token</c>)에 쓸 토큰 값을 바꿉니다. 연결돼 있는 도중에
    /// 호출해도 지금 살아있는 WebSocket이 즉시 재인증되지는 않습니다(다음 연결 시도부터 적용) —
    /// 즉시 반영이 필요하면 호출부가 <see cref="StopAsync"/> 후 <see cref="StartAsync"/>를 다시
    /// 호출해야 합니다(위 클래스 remarks "인증 헤더" 항목 참고).
    /// </summary>
    public void SetToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            _headers.Remove(TokenHeaderName);
        }
        else
        {
            _headers[TokenHeaderName] = token;
        }
    }

    /// <summary>
    /// 연결을 시도합니다. 실패해도(Runner 미실행 등) 예외를 던지지 않고
    /// <see cref="ConnectionStateChanged"/>(false)만 알린 뒤 조용히 반환합니다(위 클래스 remarks의
    /// "실패 격리" 항목 — Editor는 Runner 없이도 오프라인 편집이 가능해야 함).
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            await _connection.StartAsync(ct);
            ConnectionStateChanged?.Invoke(IsConnected);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Runner({DefaultUrl}) 모니터링 연결 실패 — 오프라인 편집을 계속합니다: {ex.Message}");
            ConnectionStateChanged?.Invoke(false);
        }
    }

    /// <summary>연결을 정상 종료합니다.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _connection.StopAsync(ct);
    }

    /// <summary>
    /// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <paramref name="nodeId"/>를
    /// Runner의 <c>MonitorHub.TriggerInject</c>(첫 클라이언트→서버 Hub 메서드)에 그대로 전달합니다.
    /// 연결돼 있지 않으면(<see cref="IsConnected"/>가 <c>false</c>) <see cref="HubConnection.InvokeAsync(string, object?, CancellationToken)"/>가
    /// 예외를 던지므로, 호출부(<c>FlowCanvasView</c>)가 미리 <see cref="IsConnected"/>를 확인하거나
    /// 예외를 처리해야 합니다 — 이 메서드 자신은 실패를 감추지 않습니다(연결 여부는 사용자가 타이틀바
    /// 배지로 이미 알 수 있으므로, 여기서 조용히 삼키면 "눌렀는데 왜 안 되는지" 원인을 알 수 없게 됨).
    /// </summary>
    public Task TriggerInjectAsync(string nodeId, CancellationToken ct = default) =>
        _connection.InvokeAsync("TriggerInject", nodeId, ct);

    /// <summary>
    /// (LK-03) Runner의 <c>MonitorHub.ReissueToken</c>을 호출해 새 토큰을 발급받고, 반환받은 값으로
    /// 곧바로 <see cref="SetToken"/>까지 호출해 다음 재연결부터 새 토큰을 쓰도록 준비합니다. 로컬
    /// 캐시 파일 저장(<c>RunnerTokenCache.SaveAsync</c>)은 호출부(<c>MainWindow</c>)의 책임입니다 —
    /// 이 클래스는 <c>Editor.Core</c>의 다른 파일 I/O 클래스와 결합하지 않고 SignalR 통신만 다룹니다.
    /// 호출 시점에 연결돼 있지 않으면(<see cref="IsConnected"/>가 <c>false</c>) 예외를 던지므로
    /// (<see cref="TriggerInjectAsync"/>와 동일한 원칙), 호출부가 미리 확인해야 합니다.
    /// </summary>
    /// <returns>Runner가 새로 발급한 토큰 값.</returns>
    public async Task<string> ReissueTokenAsync(CancellationToken ct = default)
    {
        var newToken = await _connection.InvokeAsync<string>("ReissueToken", ct);
        SetToken(newToken);
        return newToken;
    }

    /// <summary>
    /// (LK-04) <paramref name="msgId"/>(보통 <see cref="NodeErrorEvent.MsgId"/>)로 Runner의
    /// <c>MonitorHub.GetMsgTrace</c>를 호출해, 그 메시지가 지금까지 거쳐온 전체 경로를 받아옵니다.
    /// Runner가 한 번도 추적한 적이 없으면(예: Runner 재시작으로 메모리가 비워짐) <c>null</c>을
    /// 반환합니다 — 예외가 아니라 정상적인 "모른다" 응답이므로 호출부가 별도 처리 없이 그냥 표시를
    /// 생략하면 됩니다. 연결돼 있지 않으면(<see cref="IsConnected"/>가 <c>false</c>) 예외를 던지므로
    /// (<see cref="TriggerInjectAsync"/>와 동일한 원칙), 호출부가 미리 확인하거나 예외를 처리해야
    /// 합니다.
    /// </summary>
    public Task<MsgTrace?> GetMsgTraceAsync(string msgId, CancellationToken ct = default) =>
        _connection.InvokeAsync<MsgTrace?>("GetMsgTrace", msgId, ct);

    /// <summary>내부 <see cref="HubConnection"/>을 완전히 정리합니다(위 클래스 remarks의 "구독 해제" 항목).</summary>
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
