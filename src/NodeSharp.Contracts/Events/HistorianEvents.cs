namespace NodeSharp.Contracts.Events;

/// <summary>
/// Class명 : Historian 무결성 이벤트
/// 역활 및 기능 : HistorianIntegrityChecker(ED-D09)가 DB 손상을 감지·처리한 결과를 알리는 경고 이벤트
///
/// <c>NodeSharp.Runtime.HistorianIntegrityChecker</c>가 기동 시 손상을 감지했을 때 발행하는 경고
/// 이벤트입니다. <see cref="NodeErrorEvent"/>(노드 실행 중 예외, <c>Msg</c> 컨텍스트가 있는 경우)와
/// 성격이 달라 별도 레코드로 선언합니다 — 이 이벤트는 특정 노드·특정 메시지가 아니라 Historian DB
/// 파일 자체의 상태를 알립니다.
/// 설계 근거: 03번 Step맵 ED-D09, 02번 문서 8번 탭 카드12 <c>HistorianIntegrityChecker</c> 스니펫
/// (<c>EventLogWriter.WriteError</c> 참조 부분).
/// </summary>
/// <remarks>
/// <b>왜 <c>EventLogWriter</c>(Windows 이벤트 로그, <c>NodeSharp.Runner</c>) 직접 호출이 아니라
/// <see cref="Interfaces.IEventBus"/> 발행인가</b> — 02번 문서 원본 스니펫은
/// <c>EventLogWriter.WriteError</c>를 직접 호출하지만, 그 클래스는 <c>NodeSharp.Runner</c> 소속
/// (<c>RN-06b</c>)이고 <c>HistorianIntegrityChecker</c>는 <c>SqliteTagHistorian</c>(ED-D08a)과 같은
/// 계층인 <c>NodeSharp.Runtime</c>에 있어야 합니다. 프로젝트 참조 방향(<c>Runner</c>→<c>Runtime</c>이지
/// 그 반대가 아님, P0-1b 배선)상 <c>Runtime</c>이 <c>Runner</c>의 클래스를 직접 참조하면 순환 참조가
/// 됩니다(<c>CT-03a</c>가 겪었던 것과 동일한 유형의 계층 문제). <see cref="NodeErrorEvent"/>가
/// "노드 예외를 어떻게 Editor·로그까지 전달하는가"를 <c>IEventBus</c>로 이미 풀었던 것과 같은 방식으로,
/// 이 이벤트도 <c>IEventBus</c>로 발행해 두고 <c>NodeSharp.Runner</c>(또는 향후 이벤트 로그 연동 Step)가
/// 구독해 <c>EventLogWriter.WriteError</c>·Editor UI 경고로 각각 중계하도록 계층을 분리했습니다.
/// </remarks>
/// <param name="DbPath">손상이 감지된 Historian DB 파일 경로.</param>
/// <param name="RestoredFromBackup"><c>true</c>면 최신 백업으로 자동 복원됨, <c>false</c>면 백업이 없어 빈 DB로 재초기화됨.</param>
/// <param name="Message">사람이 읽을 수 있는 경고 문구(Editor 알림·이벤트 로그에 그대로 표시 가능).</param>
/// <param name="At">감지·처리 시각(UTC).</param>
/// <example>
/// <code>
/// eventBus.Publish(new HistorianIntegrityEvent(
///     DbPath: @"C:\NodeSharpRead\history.db", RestoredFromBackup: false,
///     Message: "Historian 데이터가 손상되어 초기화되었습니다.", At: DateTime.UtcNow));
/// </code>
/// </example>
public sealed record HistorianIntegrityEvent(string DbPath, bool RestoredFromBackup, string Message, DateTime At);
