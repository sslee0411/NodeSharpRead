namespace NodeSharp.Contracts.Events;

// 한글명: 노드 상태 이벤트
/// <summary>
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
/// // 4) 노드 예외 발생 — 캔버스 빨간 경고 배지 + 하단 로그 패널
/// eventBus.Publish(new NodeErrorEvent(NodeId: "n1", Message: ex.Message, StackTrace: ex.StackTrace, At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record NodeStatusEvent(string NodeId, string Fill, string Shape, string Text, DateTime At);

// 한글명: 와이어 활동 이벤트
/// <summary>메시지가 어느 와이어(<paramref name="FromNodeId"/>의 <paramref name="OutputPort"/> → <paramref name="ToNodeId"/>)를 타고 흘렀는지 나타냅니다. 캔버스 와이어 애니메이션에 사용됩니다.</summary>
public sealed record FlowActivityEvent(string FromNodeId, int OutputPort, string ToNodeId, string MsgId, DateTime At);

// 한글명: 디버그 메시지 이벤트
/// <summary>Debug 노드의 출력 1건입니다. <paramref name="MsgJson"/>은 <c>Msg.ToJson()</c> 결과이며, Editor 디버그 사이드바에 그대로 표시됩니다.</summary>
public sealed record DebugMessageEvent(string NodeId, string NodeName, string MsgJson, DateTime At);

// 한글명: 노드 오류 이벤트
/// <summary>노드 실행 중 발생한 예외 1건입니다. 캔버스 빨간 경고 배지와 하단 로그 패널에 표시됩니다.</summary>
public sealed record NodeErrorEvent(string NodeId, string Message, string? StackTrace, DateTime At);
