using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Events;

/// <summary>
/// Class명 : 시퀀스 체크포인트 알림 이벤트
/// 역활 및 기능 : Runner 기동 시 SequenceCheckpointStore에서 발견한 체크포인트를 Editor에 알리는 SignalR 페이로드(방향: Runner → Editor)
///
/// (SQ-05) Runner가 기동해 sequences.checkpoint.json을 읽었을 때, 지난 실행이 남긴 상태를 Editor에
/// 알리는 두 가지 상황에서 이 레코드를 그대로 재사용합니다(구분은 SignalR 메서드명으로만 함 —
/// <see cref="State"/> 자체는 항상 체크포인트에 저장된 그대로):
/// <list type="bullet">
/// <item><b>"sequenceAutoSafeStopped"</b>: 체크포인트가 <see cref="SequenceState.Running"/>으로
/// 남아있었다(=Runner가 그 단계 도중 크래시했다) — Runner가 이미 자동으로 안전정지 처리(체크포인트를
/// <see cref="SequenceState.Faulted"/>로 갱신)한 뒤 "이렇게 처리했다"는 정보만 알리는 것이라 Editor의
/// 응답은 필요 없습니다(정보 전달용, <c>MonitorHub.ResolveSequenceCheckpoint</c> 호출 불필요).</item>
/// <item><b>"sequenceCheckpointNeedsConfirmation"</b>: 체크포인트가 이미 <see cref="SequenceState.Faulted"/>였다
/// (=지난 실행이 정상적으로 실패로 끝났다) — 자동으로 아무 것도 하지 않고 Editor의 확인을 기다립니다.
/// Editor는 "재개/처음부터/무시" 중 하나를 골라 <c>MonitorHub.ResolveSequenceCheckpoint(SequenceId, choice)</c>로
/// 응답해야 합니다.</item>
/// </list>
/// 설계 근거: 02번 문서 8번 탭 "★ 크래시 복구(시스템 공백)" 항목의 <c>SequenceCheckpointStore</c> 설계.
/// </summary>
/// <param name="SequenceId"><see cref="Contracts.Models.SequenceDefinition.Id"/>.</param>
/// <param name="CurrentStepId">체크포인트에 기록된 마지막 단계 이름(<c>SequenceStepDto.Name</c>).</param>
/// <param name="State">체크포인트에 기록된 상태(이미 처리된 결과 — 위 목록 참고).</param>
/// <param name="At">체크포인트가 기록된 시각(UTC).</param>
public sealed record SequenceCheckpointNoticeEvent(string SequenceId, string CurrentStepId, SequenceState State, DateTime At);
