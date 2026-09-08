using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 시퀀스 체크포인트 복구 서비스
/// 역활 및 기능 : Runner 기동 시 sequences.json의 각 시퀀스마다 마지막 체크포인트를 읽어 상태별로 처리하는 1회성 기동 단계
///
/// (SQ-05) 사용자 확인(2026-09-08 세션, "SQ-05 범위" 질문 — "체크포인트 인프라만 지금 구현(권장)")에
/// 따라 구현한 클래스입니다. 02번 문서 8번 탭 "★ 크래시 복구" 카드의 의도대로 "재기동 시 Faulted
/// 상태면 Editor에 재개/처음부터/무시 확인, Running 상태로 죽었으면(=크래시) 안전 우선 원칙으로
/// 자동 안전정지 단계부터 시작"을 구현합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>★ 범위 축소 — 시퀀스 오케스트레이션 자체는 이 Step 밖</b>: <see cref="StartupSequencer"/>는
/// "sequences.json 역직렬화만 하고 내용은 버림"(자체 XML 문서의 RN-01a 범위 한정 참고)이 이미
/// 확정된 설계이며, Runner가 sequences.json을 읽어 <see cref="SequenceExecutor"/> 인스턴스를 만들어
/// 실제로 자동 실행하는 오케스트레이션 자체가 아직 존재하지 않습니다(조사 결과 발견한 구조적 공백).
/// 따라서 이 클래스도 "체크포인트를 읽어 상태별로 안내·정리하는 것"까지만 하고, 체크포인트를 실제로
/// 이어서 실행(재개)하는 것은 후속 Step(시퀀스 오케스트레이션 자체가 생긴 뒤)으로 명시적으로
/// 미룹니다 — 아래 <see cref="ResolveAsync"/>의 "재개" 분기가 아직 아무 일도 하지 않는 이유입니다.</item>
/// <item><b>sequences.json을 직접 읽는 이유</b>: <see cref="StartupSequencer"/>의 결과(<c>StartupStageResult</c>)는
/// "성공/실패"만 담고 역직렬화된 내용을 버리므로, 이 클래스는 <see cref="Worker"/>가 그 단계 성공을
/// 확인한 뒤 자체적으로 sequences.json을 다시 읽습니다(<see cref="StartupSequencer"/> 자체를 수정하지
/// 않음 — 그 클래스의 "역직렬화만" 범위를 그대로 존중).</item>
/// <item><b>Running → 자동 안전정지</b>: 크래시 직전에 실행 중이었다는 뜻이므로, 사용자에게 묻지 않고
/// 즉시 체크포인트를 <see cref="SequenceState.Faulted"/>로 덮어써 기록한 뒤(안전 우선 원칙),
/// <see cref="MonitorHub"/>를 통해 <c>"sequenceAutoSafeStopped"</c>(정보성 알림, Editor의 응답 불필요)를
/// 방송합니다.</item>
/// <item><b>Faulted → 확인 요청</b>: 이미 안전정지된 상태이므로 체크포인트를 건드리지 않고,
/// <c>"sequenceCheckpointNeedsConfirmation"</c>(Editor의 응답이 필요 — <see cref="ResolveAsync"/>를
/// <see cref="MonitorHub.ResolveSequenceCheckpoint"/>로 호출해줘야 함)을 방송합니다.</item>
/// <item><b>그 외 상태(Idle/Paused/Completed)</b>: 크래시와 무관한 정상 종료 상태이므로 아무 것도
/// 하지 않습니다.</item>
/// <item><b>StatusBroadcaster를 쓰지 않는 이유</b>: <see cref="StatusBroadcaster"/>는 <c>FlowEngine</c>의
/// <see cref="IEventBus"/>를 구독해 이벤트가 발생할 때마다 중계하는 상시 구독자인 반면, 이 알림은
/// 어떤 <c>FlowEngine</c>/<c>SequenceExecutor</c> 인스턴스와도 무관한 "기동 시 1회성" 알림이라
/// <see cref="IHubContext{THub}"/>로 직접 방송하는 편이 더 단순합니다(같은 방식을 이미 쓰는
/// <see cref="MonitorHub.ReissueToken"/>의 <c>Clients.Others.SendAsync</c> 참고).</item>
/// </list>
/// </remarks>
public sealed class SequenceCheckpointRecoveryService
{
    private readonly SequenceCheckpointStore _checkpointStore;
    private readonly IHubContext<MonitorHub> _hub;

    /// <summary>DI가 <see cref="AddSingleton{TService}"/>로 등록된 <see cref="SequenceCheckpointStore"/>(Runtime)와 <see cref="IHubContext{THub}"/>(<see cref="MonitorHub"/>)를 자동으로 주입합니다.</summary>
    public SequenceCheckpointRecoveryService(SequenceCheckpointStore checkpointStore, IHubContext<MonitorHub> hub)
    {
        _checkpointStore = checkpointStore;
        _hub = hub;
    }

    /// <summary>
    /// <paramref name="baseDirectory"/>의 sequences.json을 읽어 각 시퀀스의 마지막 체크포인트를
    /// 클래스 XML 문서의 규칙(Running→자동 안전정지, Faulted→확인 요청, 그 외→무시)대로 처리합니다.
    /// sequences.json이 없거나 손상됐으면(예: StartupSequencer의 그 단계가 이미 실패로 기록됨)
    /// <see cref="StartupSequencer"/>와 동일한 "격리" 원칙으로 조용히 아무 것도 하지 않습니다.
    /// </summary>
    public async Task RecoverAsync(string baseDirectory, CancellationToken ct)
    {
        List<SequenceDefinition>? definitions;
        try
        {
            var path = Path.Combine(baseDirectory, "sequences.json");
            if (!File.Exists(path))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(path, ct);
            definitions = JsonSerializer.Deserialize<List<SequenceDefinition>>(json);
        }
        catch (Exception)
        {
            // StartupSequencer의 "단계별 격리" 원칙과 동일 — sequences.json이 손상돼도 Runner
            // 기동 자체를 막지 않고, 이 1회성 복구 단계만 조용히 건너뛴다.
            return;
        }

        if (definitions is null)
        {
            return;
        }

        foreach (var definition in definitions)
        {
            var checkpoint = await _checkpointStore.TryLoadAsync(definition.Id, baseDirectory, ct);
            if (checkpoint is null)
            {
                continue;
            }

            switch (checkpoint.State)
            {
                case SequenceState.Running:
                    var safeStopped = checkpoint with { State = SequenceState.Faulted, At = DateTime.UtcNow };
                    await _checkpointStore.SaveAsync(safeStopped, baseDirectory, ct);
                    await BroadcastAsync("sequenceAutoSafeStopped", safeStopped);
                    break;

                case SequenceState.Faulted:
                    await BroadcastAsync("sequenceCheckpointNeedsConfirmation", checkpoint);
                    break;

                default:
                    // Idle/Paused/Completed — 크래시와 무관한 정상 종료 상태, 아무 것도 하지 않는다.
                    break;
            }
        }
    }

    /// <summary>
    /// <see cref="MonitorHub.ResolveSequenceCheckpoint"/>가 Editor의 확인 응답(재개/처음부터/무시)을
    /// 그대로 위임합니다.
    /// </summary>
    /// <param name="sequenceId">확인 대상 시퀀스의 <see cref="SequenceDefinition.Id"/>.</param>
    /// <param name="choice">
    /// <c>"restart"</c>(처음부터 — 체크포인트를 지워 다음 기동부터는 복구 대상에서 제외),
    /// <c>"ignore"</c>(무시 — 체크포인트를 그대로 남겨둠, 다음 기동 시 다시 확인 요청됨),
    /// <c>"resume"</c>(재개 — ★ 범위 축소, 클래스 XML 문서 참고: 시퀀스 오케스트레이션 자체가
    /// 생기는 후속 Step까지 아무 것도 하지 않음) 중 하나. 그 외 값은 <c>"ignore"</c>와 동일하게 무시합니다.
    /// </param>
    public async Task ResolveAsync(string sequenceId, string choice, string baseDirectory, CancellationToken ct)
    {
        if (string.Equals(choice, "restart", StringComparison.OrdinalIgnoreCase))
        {
            await _checkpointStore.ClearAsync(sequenceId, baseDirectory, ct);
        }

        // "resume"은 ★ 범위 축소(클래스 XML 문서 참고)로 아직 아무 것도 하지 않고,
        // "ignore"(및 그 외 알 수 없는 값)도 체크포인트를 그대로 남겨두는 것이 곧 "무시"이므로
        // 별도 분기가 필요 없다.
    }

    private Task BroadcastAsync(string method, SequenceCheckpointStore.Checkpoint checkpoint)
    {
        var evt = new SequenceCheckpointNoticeEvent(checkpoint.SequenceId, checkpoint.CurrentStepId, checkpoint.State, checkpoint.At);
        return _hub.Clients.All.SendAsync(method, evt);
    }
}
