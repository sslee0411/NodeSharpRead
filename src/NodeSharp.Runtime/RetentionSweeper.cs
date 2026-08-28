using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Util.Messaging;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 보관 기간 정리 배치
/// 역활 및 기능 : 매일 새벽 1회 Historian 원본/집계 데이터 중 보관 기간이 지난 행을 삭제하는 공유 서비스
///
/// (ED-D10) 02번 설계문서 8번 탭 카드14 <c>RetentionSweeper</c> 스니펫을 그대로 옮겼습니다 —
/// <see cref="ITagHistorian.PurgeOlderThanAsync"/>(원본, <see cref="RetentionPolicy.RawDataRetention"/>
/// 기준)와 <see cref="ITagHistorian.PurgeAggregateOlderThanAsync"/>(집계,
/// <see cref="RetentionPolicy.AggregatedRetention"/> 기준)를 각각 별도 컷오프로 호출합니다.
/// 설계 근거: 02번 문서 8번 탭 카드14.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>감사 로그 정리는 델리게이트로 범위 축소</b> — 원본 스니펫은
/// <c>_auditLog.ArchiveAndPurgeOlderThanAsync(...)</c>도 함께 호출하지만, 그 대상인 감사 로그 저장소
/// (<c>AuditEntry</c>/<c>OP-01</c>, 아직 <c>⏳ 대기</c>)가 프로젝트 어디에도 없습니다(<c>ED-D07b</c>·
/// <c>LK-03</c>에서도 동일하게 확인된 공백). 이 Step은 <c>ED-D07b</c>/<c>ED-D08b</c>처럼 존재하지
/// 않는 클래스에 "컴파일"이 걸린 경우가 아니라 "감사 로그를 어떻게 지우는가"라는 동작 하나만 비어
/// 있는 경우라, <c>HistorianIntegrityChecker.RestoreFromLatestBackupAction</c>(ED-D09)과 동일한 관례로
/// <see cref="PurgeAuditLogAction"/> 델리게이트를 두어 완료 기준("각 보관 기간을 초과한 데이터가
/// 삭제되는지")의 감사 로그 부분까지 지금 델리게이트 주입으로 테스트 가능하게 했습니다 — 기본값
/// <c>null</c>은 "정리 안 함"(감사 로그 저장소 자체가 없으므로 당연한 동작)이고, <c>OP-01</c> 완성
/// 후 실제 구현을 그대로 주입하면 이 클래스는 손대지 않아도 됩니다.</item>
/// <item><b>압축 백업 후 삭제는 이 1차 구현 범위 밖</b> — 원본 스니펫 주석 "삭제 전 원본은 압축
/// 백업으로 별도 보관 후 삭제"는 그 백업 대상(<c>OP-09</c> 다세대 백업, 아직 <c>⏳ 대기</c>)이 없어
/// 지금은 압축 백업 없이 바로 삭제합니다 — 완료 기준 자체("보관 기간을 초과한 데이터가 삭제되는지")는
/// 삭제만 요구하므로 이 범위 축소로도 충분히 검증 가능합니다(<see cref="SqliteTagHistorian"/> 클래스
/// remarks에도 동일하게 기록).</item>
/// <item><b><see cref="TagIds"/>·<see cref="Scheduler"/> 없이 <see cref="ITagHistorian"/>만 주입</b> —
/// <see cref="TagAggregationJob"/>과 달리 Purge는 태그별이 아니라 DB 전체를 대상으로 하므로
/// (<see cref="ITagHistorian.PurgeOlderThanAsync"/> 시그니처에 <c>tagId</c>가 없음) 태그 목록 주입이
/// 필요 없습니다. <see cref="Scheduler"/>는 <see cref="DeviceMapPoller"/>/<see cref="TagAggregationJob"/>과
/// 동일한 관례로 <c>null</c>이면 기본 <see cref="AsyncSchedulerAdapter"/>를 사용합니다.</item>
/// <item><b><see cref="UtcNowProvider"/></b>는 <see cref="TagAggregationJob"/>과 동일한 이유(실제 시간
/// 경과를 기다리지 않고 컷오프 계산 로직 자체를 결정적으로 테스트하기 위함, RT-08 타이밍 테스트
/// 교훈 참고)로 추가했습니다.</item>
/// <item><b><see cref="RunOnceAsync"/></b>는 02번 설계 문서(<c>DiskSpaceMonitor</c> 카드, "위험 단계에서
/// <c>RetentionSweeper.RunOnceAsync</c> 즉시 강제 실행")가 이미 이름까지 지정해둔 메서드입니다 — 매일
/// 새벽 Cron 콜백과 <c>RN-05b-b</c>(디스크 공간 위험 시 강제 실행, 아직 <c>⏳ 대기</c>)의 향후 강제
/// 실행 경로가 같은 메서드를 공유하도록 미리 이 이름으로 공개했습니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var sweeper = new RetentionSweeper(historian) { Id = "retention-sweeper" };
/// await sweeper.StartAsync(CancellationToken.None);   // 매일 새벽 1시 Cron 등록
///
/// // 완료 기준 직접 검증(스케줄러를 거치지 않고 즉시 호출)
/// var result = await sweeper.RunOnceAsync(CancellationToken.None);
/// // result.RawDeleted / result.AggregateDeleted / result.AuditLogDeleted
/// </code>
/// </example>
public sealed class RetentionSweeper : ISharedServiceNode
{
    private readonly ITagHistorian _historian;
    private IScheduler? _activeScheduler;

    /// <inheritdoc />
    /// <remarks>이 정리 배치 자체의 식별자입니다 — 같은 배치를 가리키는 인스턴스는 항상 같은 Id를 가져야 합니다(<see cref="ISharedServiceNode.Id"/> 문서 참고).</remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>적용할 보관 기간 정책. 지정하지 않으면 <see cref="RetentionPolicy.Default"/>(원본 30일/집계 1년/감사로그 1년)를 사용합니다.</summary>
    public RetentionPolicy Policy { get; init; } = RetentionPolicy.Default;

    /// <summary>(<see cref="DeviceMapPoller.Scheduler"/>와 동일한 관례) 지정하지 않으면 <see cref="StartAsync"/>가 기본 <see cref="AsyncSchedulerAdapter"/>를 직접 생성합니다.</summary>
    public IScheduler? Scheduler { get; set; }

    /// <summary>(클래스 remarks의 "UtcNowProvider" 항목 참고) 기본값은 실제 <c>DateTime.UtcNow</c>이며, 테스트에서만 결정적 시각으로 교체합니다.</summary>
    public Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// (클래스 remarks의 "감사 로그 정리는 델리게이트로 범위 축소" 항목 참고) 감사 로그를
    /// <see cref="RetentionPolicy.AuditLogRetention"/> 컷오프 이전으로 정리하는 동작. 기본값
    /// <c>null</c>은 "정리 안 함"(<c>OP-01</c> 감사 로그 저장소가 아직 없음)과 동일하게 동작합니다.
    /// 반환값은 삭제된 건수입니다.
    /// </summary>
    public Func<DateTime, CancellationToken, Task<int>>? PurgeAuditLogAction { get; set; }

    /// <summary>이력을 정리할 <see cref="ITagHistorian"/>을 받습니다.</summary>
    public RetentionSweeper(ITagHistorian historian) =>
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));

    /// <summary>
    /// <see cref="Scheduler"/>(없으면 기본 <see cref="AsyncSchedulerAdapter"/>)에 <see cref="Id"/>를
    /// ownerId 삼아 매일 새벽 1시(<c>"0 0 1 * * *"</c>)에 <see cref="RunOnceAsync"/>를 실행하도록
    /// 등록합니다.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        _activeScheduler = Scheduler ?? new AsyncSchedulerAdapter();
        _activeScheduler.ScheduleCron(Id, "0 0 1 * * *", () => RunOnceAsync(ct));
        return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="Policy"/>의 각 보관 기간을 기준으로 원본·집계·(주입됐다면) 감사 로그를 정리하고,
    /// 각각 삭제된 건수를 담은 <see cref="RetentionSweepResult"/>를 반환합니다. 02번 설계 문서
    /// <c>DiskSpaceMonitor</c>가 위험 단계에서 강제로 즉시 호출하는 메서드이기도 합니다(클래스
    /// remarks 참고).
    /// </summary>
    public async Task<RetentionSweepResult> RunOnceAsync(CancellationToken ct)
    {
        var now = UtcNowProvider();

        var rawDeleted = await _historian.PurgeOlderThanAsync(now - Policy.RawDataRetention, ct).ConfigureAwait(false);
        var aggregateDeleted = await _historian.PurgeAggregateOlderThanAsync(now - Policy.AggregatedRetention, ct).ConfigureAwait(false);

        var auditLogDeleted = PurgeAuditLogAction is null
            ? 0
            : await PurgeAuditLogAction(now - Policy.AuditLogRetention, ct).ConfigureAwait(false);

        return new RetentionSweepResult(rawDeleted, aggregateDeleted, auditLogDeleted);
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        _activeScheduler?.Unschedule(Id);
        _activeScheduler = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Class명 : 보관 기간 정리 결과
/// 역활 및 기능 : <see cref="RetentionSweeper.RunOnceAsync"/> 1회 실행에서 각 데이터 종류별로 삭제된 건수
/// </summary>
/// <param name="RawDeleted">삭제된 원본(Historian) 행 수.</param>
/// <param name="AggregateDeleted">삭제된 집계(Historian) 행 수.</param>
/// <param name="AuditLogDeleted">삭제된 감사 로그 건수(<see cref="RetentionSweeper.PurgeAuditLogAction"/>이 <c>null</c>이면 항상 0).</param>
public sealed record RetentionSweepResult(int RawDeleted, int AggregateDeleted, int AuditLogDeleted);
