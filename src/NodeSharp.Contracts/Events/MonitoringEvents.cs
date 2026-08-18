namespace NodeSharp.Contracts.Events;

/// <summary>
/// Class명 : 노드 상태 이벤트
/// 역활 및 기능 : 노드가 자신의 상태(색/모양/텍스트)를 알리는 실시간 모니터링 이벤트
///
/// Runner가 Editor로 실시간 스트리밍하는 4가지 모니터링 이벤트(노드 상태/와이어 활동/디버그 출력/노드
/// 에러)를 담는 순수 데이터 레코드 모음입니다. Node-RED의 <c>node.status(...)</c>·캔버스 와이어
/// 애니메이션·디버그 사이드바·빨간 에러 배지에 각각 대응합니다.
/// 설계 근거: 02번 문서 3번 탭 카드 7(<c>StatusBroadcaster</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>이 4개 레코드는 <c>NodeSharp.Runner</c>의 <c>StatusBroadcaster</c>가 내부 <c>EventBus</c> 구독을
/// SignalR Hub(<c>LK-02</c>, 아직 미구현)로 그대로 중계하는 페이로드입니다 — Runtime 구체 타입을
/// 참조하지 않는 순수 데이터라 Contracts에 안전하게 선언할 수 있습니다.</item>
/// <item><see cref="TagValueUpdatedEvent"/>(8번 탭 카드 15, 값 변경 시만·초당 5회 스로틀)는 <c>CT-05b</c>에서
/// 별도로 정의합니다 — PLC 태그 전용 이벤트라 이 파일의 "노드 상태/에러 계열"과 성격이 다릅니다.</item>
/// <item><b>(LK-04) <see cref="NodeErrorEvent"/> 확장</b>: 원래 <c>NodeId</c>/<c>Message</c>/
/// <c>StackTrace</c>/<c>At</c> 4개 필드만 있었고, 이 레코드를 실제로 발행(<c>Publish</c>)하는 코드가
/// 프로젝트 어디에도 없었습니다(<c>NodeSharp.Runtime.FlowEngine.DispatchOneAsync</c>가 대상 노드의
/// <c>OnInputAsync</c> 호출을 <c>try/catch</c>로 감싸지 않아 예외가 그대로 호출부까지 전파됐음 — 이
/// 격리는 <c>RT-04a</c> 설계 당시 "이 Step 완료 기준은 정상 경로만 요구"로 의도적으로 범위 밖에 남겨둔
/// 부분이었습니다). LK-04(근본 원인 분석/Msg Trace)가 그 격리를 추가하면서, 02번 설계 문서 7번 탭
/// 카드5의 <c>NodeErrorDetail</c>이 요구하는 정보(노드 이름/타입, 예외 타입, 에러 시점 msg 스냅샷) 중
/// "실시간 push로 바로 알아야 하는 부분"을 이 레코드에 함께 실어 보냅니다 — 나머지(노드 설정값
/// 스냅샷, msg가 거쳐온 전체 경로)는 별도 <c>NodeErrorDetail</c> 레코드를 새로 만들지 않고,
/// <see cref="MsgId"/>를 키로 <c>MonitorHub.GetMsgTrace</c>(신규, on-demand pull)를 호출해
/// <c>Models.MsgTrace</c>를 받아오는 방식으로 나눴습니다 — "노드가 몇 개를 거쳐왔는지"는 이 노드
/// 하나의 에러 시점에만 필요한 게 아니라 정상 경로를 포함해 이미 <see cref="FlowActivityEvent"/> 이력
/// 전체에서 계산 가능한 정보라, 에러 이벤트 페이로드에 항상 끼워 보내기보다 필요할 때만 조회하는 편이
/// 낫다고 판단했습니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 노드가 자신의 상태를 알림 — Node-RED의 this.status({fill,shape,text})와 동일한 사용감
/// eventBus.Publish(new NodeStatusEvent(NodeId: "n1", Fill: "green", Shape: "dot", Text: "연결됨", At: DateTime.UtcNow));
///
/// // 2) 메시지가 와이어를 타고 흘렀음을 알림 — 캔버스 와이어 애니메이션에 사용
/// eventBus.Publish(new FlowActivityEvent(FromNodeId: "n1", OutputPort: 0, ToNodeId: "n2", MsgId: msg.Id, At: DateTime.UtcNow));
///
/// // 3) Debug 노드 출력 — 디버그 사이드바에 표시
/// eventBus.Publish(new DebugMessageEvent(NodeId: "n3", NodeName: "디버그", MsgJson: msg.ToJson(), At: DateTime.UtcNow));
///
/// // 4) 노드 예외 발생 — 캔버스 빨간 경고 배지 + 하단 로그 패널 (LK-04: FlowEngine.DispatchOneAsync가 실제로 발행)
/// eventBus.Publish(new NodeErrorEvent(
///     NodeId: "n1", NodeName: "단위변환", NodeType: "function", ExceptionType: ex.GetType().Name,
///     Message: ex.Message, StackTrace: ex.StackTrace, MsgId: msg.Id, MsgSnapshotJson: msg.ToJson(),
///     At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record NodeStatusEvent(string NodeId, string Fill, string Shape, string Text, DateTime At);

/// <summary>
/// Class명 : 와이어 활동 이벤트
/// 역활 및 기능 : 메시지가 어느 와이어를 타고 흘렀는지 알려 캔버스 와이어 애니메이션에 쓰이는 이벤트
///
/// 메시지가 어느 와이어(<paramref name="FromNodeId"/>의 <paramref name="OutputPort"/> → <paramref name="ToNodeId"/>)를 타고 흘렀는지 나타냅니다. 캔버스 와이어 애니메이션에 사용됩니다.
/// (LK-04) <see cref="MsgId"/> 기준으로 <c>NodeSharp.Runner.Core.MsgTraceStore</c>가 이 이벤트 이력을
/// 누적해 <c>Models.MsgTrace</c>(msg 하나가 지나온 전체 경로)를 만듭니다.
/// </summary>
public sealed record FlowActivityEvent(string FromNodeId, int OutputPort, string ToNodeId, string MsgId, DateTime At);

/// <summary>
/// Class명 : 디버그 메시지 이벤트
/// 역활 및 기능 : Debug 노드의 출력 1건을 Editor 디버그 사이드바로 전달하는 이벤트
///
/// Debug 노드의 출력 1건입니다. <paramref name="MsgJson"/>은 <c>Msg.ToJson()</c> 결과이며, Editor 디버그 사이드바에 그대로 표시됩니다.
/// </summary>
public sealed record DebugMessageEvent(string NodeId, string NodeName, string MsgJson, DateTime At);

/// <summary>
/// Class명 : 노드 오류 이벤트
/// 역활 및 기능 : 노드 실행 중 발생한 예외 1건을 캔버스 경고 배지·로그 패널·에러 상세 패널로 전달하는 이벤트
///
/// 노드 실행 중 발생한 예외 1건입니다. 캔버스 빨간 경고 배지와 하단 로그 패널(에러 상세 포함)에
/// 표시됩니다. (LK-04) <c>NodeSharp.Runtime.FlowEngine.DispatchOneAsync</c>가 대상 노드의
/// <c>OnInputAsync</c> 호출을 <c>try/catch</c>로 감싸 예외를 여기서 흡수하고(한 노드의 실패가 다른
/// Wire·다른 메시지 처리를 막지 않도록 격리 — <c>RT-02b</c>/<c>RT-03</c>의 "예외 하나가 전체를 막지
/// 않는다" 원칙을 메시지 라우팅 경로에도 적용) 이 이벤트를 발행하는 유일한 발행처입니다.
/// </summary>
/// <param name="NodeId">에러가 발생한 노드의 인스턴스 Id(<see cref="Interfaces.IFlowNode.Id"/>).</param>
/// <param name="NodeName">에러가 발생한 노드의 캔버스 표시 이름(<see cref="Interfaces.IFlowNode.Name"/>) — "어느 노드"인지 노드 Id(GUID류)보다 사람이 바로 알아볼 수 있게.</param>
/// <param name="NodeType">에러가 발생한 노드의 타입 이름(<see cref="Interfaces.IFlowNode.Type"/>, 예: <c>"function"</c>).</param>
/// <param name="ExceptionType">발생한 예외의 CLR 타입 이름(<c>ex.GetType().Name</c>, 예: <c>"NullReferenceException"</c>) — 코드를 보지 않고도 "무엇이 잘못됐는지" 종류를 바로 구분.</param>
/// <param name="Message">예외 메시지(<c>ex.Message</c>).</param>
/// <param name="StackTrace">예외 스택 트레이스(<c>ex.StackTrace</c>) — 없을 수 있음(예: 일부 커스텀 예외).</param>
/// <param name="MsgId">에러를 유발한 <c>Msg.Id</c> — <see cref="FlowActivityEvent.MsgId"/>와 동일한 값이라 <c>MonitorHub.GetMsgTrace(MsgId)</c>로 이 메시지가 거쳐온 전체 경로를 조회할 수 있습니다("Msg Trace로 에러 발생 노드와 해당 시점 Msg 내용까지 역추적", 03번 Step맵 LK-04 완료 기준).</param>
/// <param name="MsgSnapshotJson">에러 발생 직전(<c>OnInputAsync</c> 호출 직전) 이 노드가 실제로 받은 <c>Msg</c>의 전체 내용(<c>Msg.ToJson()</c>) — 노드가 처리 도중 필드를 바꾸다 예외를 던져도 "받았을 때의 값"을 그대로 보여줍니다.</param>
/// <param name="At">에러 발생 시각(UTC).</param>
public sealed record NodeErrorEvent(
    string NodeId,
    string NodeName,
    string NodeType,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string MsgId,
    string MsgSnapshotJson,
    DateTime At);
