using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Events;

/// <summary>
/// Class명 : 태그 값 갱신 이벤트
/// 역활 및 기능 : 태그 값이 실제로 바뀔 때만 발행되는 캔버스 실시간 오버레이 이벤트
///
/// 태그 값이 실제로 바뀔 때만 발행되는 캔버스 실시간 오버레이 이벤트입니다. <c>DeviceMapPoller</c>가
/// 폴링 캐시 갱신 직후 이전 값과 다를 때만 발행하며, 최대 초당 5회로 스로틀됩니다.
/// 설계 근거: 02번 문서 8번 탭 카드 15(캔버스 ↔ 구조 설정 연동).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>StatusBroadcaster</c>(3번 탭 카드 7)가 이 이벤트를 구독해 SignalR로 Editor에 push하면,
/// <c>CanvasViewModel</c>이 <see cref="TagId"/>로 자신의 PLC Tag Read/Write 노드를 찾아 오버레이
/// 배지를 갱신합니다(TagId → NodeId 역인덱스는 <c>IStructureService.FindNodesByTagRef</c> 결과를
/// 캐시해 매번 순회하지 않습니다).</item>
/// <item>Dashboard 위젯(<c>DB-01a~f</c>)도 이 이벤트를 공통 구독해 게이지/차트/텍스트 값을 갱신합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // DeviceMapPoller가 값이 실제로 바뀐 태그에 대해서만 발행(같은 값 반복 발행 방지)
/// if (previous != current)
///     eventBus.Publish(new TagValueUpdatedEvent(TagId: "tag-1", Value: current, Alarm: AlarmLevel.HH, At: DateTime.UtcNow));
///
/// // 알람이 없는 정상 값은 Alarm이 null
/// eventBus.Publish(new TagValueUpdatedEvent(TagId: "tag-2", Value: 42, Alarm: null, At: DateTime.UtcNow));
///
/// // 이산 상태 태그의 특정값 일치 알람(EQ, ★ 사용자 요청 v2.50 신설) — 상태코드가 3(고장)과 일치
/// eventBus.Publish(new TagValueUpdatedEvent(TagId: "tag-3", Value: 3, Alarm: AlarmLevel.EQ, At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record TagValueUpdatedEvent(string TagId, object? Value, AlarmLevel? Alarm, DateTime At);

/// <summary>
/// Class명 : 알람 발생 이벤트
/// 역활 및 기능 : 알람이 새로 발생했을 때만 발행되는 이벤트
///
/// 알람이 새로 발생했을 때만 발행되는 이벤트입니다(<c>AlarmStateManager.Evaluate</c>가 같은 알람을
/// 반복 발행하지 않도록 관리). 이 문서(02번) 안에서 8곳이 이 타입을 언급했지만 정식 <c>record</c>
/// 선언이 없었고, 11번 탭 <c>SequenceExecutor</c> 예시 코드는 존재하지 않는 <c>AlarmSeverity</c>
/// 타입까지 참조하고 있어(v1.64에서 발견) <see cref="AlarmLevel"/>을 그대로 재사용하도록 정리했습니다
/// — <c>NodeRef</c>(CT-04b)와 같은 유형의 문서 공백입니다.
/// 설계 근거: 02번 문서 8번 탭 카드 11(v1.64 보강).
/// </summary>
/// <remarks>
/// 7번 탭 모니터링(캔버스 빨간 배지), 9번 탭 알림 채널 노드(Email/SMS/Webhook), 11번 탭
/// <c>SequenceExecutor</c>(<see cref="Level"/>이 <see cref="AlarmLevel.HH"/>이고 감시 대상 태그면
/// 자동 안전정지)가 각각 이 이벤트를 구독합니다.
/// </remarks>
/// <example>
/// <code>
/// // AlarmStateManager.Evaluate()가 신규 알람 발생 시에만 발행
/// eventBus.Publish(new AlarmRaisedEvent(TagId: "tag-1", Level: AlarmLevel.HH, Value: 96.2, At: DateTime.UtcNow));
///
/// // SequenceExecutor가 감시 대상 태그의 HH 알람만 구독해 자동 안전정지
/// eventBus.Subscribe&lt;AlarmRaisedEvent&gt;(e =&gt;
/// {
///     if (Definition.WatchedTagIds.Contains(e.TagId) &amp;&amp; e.Level == AlarmLevel.HH)
///         _ = AbortAsync();
/// });
///
/// // 이산 상태 태그의 특정값 불일치 알람(NE, ★ 사용자 요청 v2.50 신설) — 상태코드가 1(정상)이 아님
/// eventBus.Publish(new AlarmRaisedEvent(TagId: "tag-3", Level: AlarmLevel.NE, Value: 3, At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record AlarmRaisedEvent(string TagId, AlarmLevel Level, double Value, DateTime At);

/// <summary>
/// Class명 : 알람 해제 이벤트
/// 역활 및 기능 : 활성 알람이 정상 범위로 돌아와 해제됐을 때만 발행되는 이벤트
///
/// (ED-D07a) 값이 더 이상 어떤 임계값/비교값 조건도 만족하지 않아 <c>AlarmStateManager</c>의 활성
/// 알람 목록에서 제거될 때만 발행되는 이벤트입니다. 02번 문서 8번 탭 카드11 원본 스니펫의
/// "AlarmClearedEvent 발행"이라는 주석뿐이던 자리를 <see cref="AlarmRaisedEvent"/>(v1.64 보강)와
/// 동일한 근거로 정식 <c>record</c>로 선언했습니다 — 필드 구성은 <see cref="AlarmRaisedEvent"/>에서
/// 알람 판정 시점의 값·레벨이 의미 없어지므로(더 이상 알람 상태가 아니므로) 그 둘을 빼고 최소화했습니다.
/// 설계 근거: 02번 문서 8번 탭 카드 11.
/// </summary>
/// <remarks>7번 탭 모니터링(캔버스 빨강 배지 해제)이 <see cref="AlarmRaisedEvent"/>와 함께 구독합니다.</remarks>
/// <example>
/// <code>
/// // AlarmStateManager.Evaluate()가 활성 알람이 정상 범위로 돌아왔을 때만 발행
/// eventBus.Publish(new AlarmClearedEvent(TagId: "tag-1", At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record AlarmClearedEvent(string TagId, DateTime At);
