using Microsoft.Data.Sqlite;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : Historian 무결성 검사기
/// 역활 및 기능 : 기동 시 1회 Historian DB의 SQLite <c>PRAGMA integrity_check</c>를 실행하고, 손상되어
/// 있으면 최신 백업으로 복원하거나(백업이 없으면) 빈 DB로 재초기화하는 기동 시 자가 복구 검사기
///
/// (ED-D09) 02번 설계문서 8번 탭 카드12 <c>HistorianIntegrityChecker</c> 스니펫을 그대로 옮겼습니다 —
/// SQLite 표준 기능인 <c>PRAGMA integrity_check</c>를 그대로 재사용해 별도 체크섬 구현이 필요 없고,
/// 이 검사는 기동 시 1회만 수행합니다(매번 하기엔 비용이 커 실시간 상시 검사는 안 함).
/// 설계 근거: 02번 문서 8번 탭 카드12.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>백업 복원은 실제 구현을 기다리지 않고 델리게이트로 범위 축소</b> — 원본
/// 스니펫의 "최신 백업 자동 복원"은 다세대 백업 기능(<c>OP-09</c>, Phase 14, 아직 <c>⏳ 대기</c>)이
/// 있어야 진짜 백업 파일을 찾을 수 있습니다. 이 Step은 <c>ED-D07b</c>/<c>ED-D08b</c>처럼 존재하지 않는
/// 클래스에 "컴파일"이 걸려 있는 게 아니라 "백업 원본을 어디서 가져오는가"라는 동작 하나만 비어 있는
/// 경우라, <c>DeviceMapPoller.BlockReadAction</c>과 동일한 관례로
/// <see cref="RestoreFromLatestBackupAction"/> 델리게이트를 두어 완료 기준("손상 시 최신 백업 자동
/// 복원, 백업조차 없으면 빈 DB 재초기화")을 지금 전부 테스트로 증명할 수 있게 했습니다 — 기본값
/// <c>null</c>은 "백업 없음"과 동일하게 동작(항상 재초기화 경로)하고, <c>OP-09</c> 완성 후 실제 구현을
/// 그대로 주입하면 이 클래스는 손대지 않아도 됩니다.</item>
/// <item><b><see cref="IntegrityCheckAction"/>은 반대로 기본값이 이미 실제 동작</b> — <c>PRAGMA
/// integrity_check</c> 자체는 <c>Microsoft.Data.Sqlite</c>(ED-D08a에서 이미 참조 추가)만으로 지금
/// 완전히 구현 가능하므로, 델리게이트가 <c>null</c>이면 실제 SQLite 파일을 열어 검사합니다(테스트에서만
/// 필요 시 페이크로 교체). DB 파일이 아직 없으면(최초 기동) 손상이 아니라 "정상"으로 취급합니다.</item>
/// <item><b>경고는 <see cref="IEventBus"/>로 발행 — <c>EventLogWriter</c> 직접 호출 아님</b> —
/// <see cref="HistorianIntegrityEvent"/> XML 주석의 "왜 IEventBus 발행인가" 항목 참고(프로젝트 참조
/// 방향상 <c>NodeSharp.Runtime</c>이 <c>NodeSharp.Runner</c>의 <c>EventLogWriter</c>를 직접 참조할 수
/// 없음, <c>CT-03a</c>와 동일한 유형의 계층 문제를 이번엔 별도 Step 신설 없이 이벤트 버스 경유로
/// 해결).</item>
/// <item><b>빈 DB 재초기화는 <see cref="SqliteTagHistorian"/>을 재사용</b> — 손상된 파일을 지우고
/// <c>new SqliteTagHistorian(DbPath)</c>를 호출하면 그 생성자의 <c>EnsureSchema()</c>가 빈 스키마를
/// 그대로 다시 만들어 주므로, 스키마 생성 로직을 이 클래스에 중복 작성하지 않았습니다.</item>
/// <item><b><see cref="ISharedServiceNode"/>를 구현하지 않음</b> — 이 검사는 주기 실행이나 배포 중
/// 계속 살아있는 리소스가 아니라 "기동 시 1회"만 수행하는 동작이라(설계 문서에도 명시), <c>RN-01a</c>
/// <c>StartupSequencer</c>와 같은 성격의 평범한 클래스로 두고 <see cref="CheckAndRepairAsync"/> 하나만
/// 공개했습니다 — Runner 기동 시퀀스(<c>RN-01a</c>/<c>RN-02</c> 계열)에 이어 붙이는 배선은 후속 Step
/// 몫입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var checker = new HistorianIntegrityChecker { DbPath = @"C:\NodeSharpRead\history.db" };
/// var outcome = await checker.CheckAndRepairAsync(CancellationToken.None);
/// // outcome == Ok / RestoredFromBackup / ReinitializedEmpty
/// </code>
/// </example>
public sealed class HistorianIntegrityChecker
{
    /// <summary>검사·복구 대상 Historian DB 파일 경로.</summary>
    public string DbPath { get; init; } = string.Empty;

    /// <summary>결과 경고를 발행할 <see cref="IEventBus"/>. 지정하지 않으면 기본 <see cref="EventBusAdapter"/>를 사용합니다.</summary>
    public IEventBus? EventBus { get; set; }

    /// <summary>
    /// (클래스 remarks의 "IntegrityCheckAction" 항목 참고) 손상 여부를 확인하는 동작. 기본값
    /// <c>null</c>이면 실제 <c>PRAGMA integrity_check</c>를 실행합니다. 반환값 <c>true</c>는 정상,
    /// <c>false</c>는 손상을 뜻합니다.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? IntegrityCheckAction { get; set; }

    /// <summary>
    /// (클래스 remarks의 "백업 복원은 델리게이트로 범위 축소" 항목 참고) 최신 백업으로 복원을
    /// 시도하는 동작. 기본값 <c>null</c>은 "사용 가능한 백업 없음"과 동일하게 동작합니다. 반환값
    /// <c>true</c>는 복원 성공, <c>false</c>는 복원할 백업이 없음(또는 실패)을 뜻합니다.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? RestoreFromLatestBackupAction { get; set; }

    /// <summary>
    /// <see cref="DbPath"/>의 무결성을 검사하고, 손상돼 있으면 <see cref="RestoreFromLatestBackupAction"/>으로
    /// 복원을 시도하거나(실패/미지정 시) 빈 DB로 재초기화합니다. 손상이 감지된 두 경우 모두
    /// <see cref="HistorianIntegrityEvent"/>를 발행합니다.
    /// </summary>
    public async Task<HistorianIntegrityOutcome> CheckAndRepairAsync(CancellationToken ct)
    {
        var ok = await RunIntegrityCheckAsync(ct).ConfigureAwait(false);
        if (ok)
        {
            return HistorianIntegrityOutcome.Ok;
        }

        var bus = EventBus ?? new EventBusAdapter();

        var restored = RestoreFromLatestBackupAction is null
            ? false
            : await RestoreFromLatestBackupAction(DbPath, ct).ConfigureAwait(false);

        if (restored)
        {
            bus.Publish(new HistorianIntegrityEvent(
                DbPath: DbPath, RestoredFromBackup: true,
                Message: $"Historian DB({DbPath})가 손상되어 최신 백업으로 자동 복원되었습니다.", At: DateTime.UtcNow));
            return HistorianIntegrityOutcome.RestoredFromBackup;
        }

        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }

        _ = new SqliteTagHistorian(DbPath);   // EnsureSchema()가 빈 스키마로 재생성(클래스 remarks 참고)

        bus.Publish(new HistorianIntegrityEvent(
            DbPath: DbPath, RestoredFromBackup: false,
            Message: $"Historian DB({DbPath})가 손상되었고 사용 가능한 백업이 없어 빈 DB로 초기화되었습니다.", At: DateTime.UtcNow));
        return HistorianIntegrityOutcome.ReinitializedEmpty;
    }

    private async Task<bool> RunIntegrityCheckAsync(CancellationToken ct)
    {
        if (IntegrityCheckAction is not null)
        {
            return await IntegrityCheckAction(DbPath, ct).ConfigureAwait(false);
        }

        if (!File.Exists(DbPath))
        {
            return true;   // 최초 기동(파일 없음)은 손상이 아니라 정상으로 취급
        }

        try
        {
            await using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = (string?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false;   // 파일 자체를 열 수 없으면(SQLite 형식이 아님 등) 손상으로 간주
        }
    }
}

/// <summary>
/// Class명 : Historian 무결성 검사 결과
/// 역활 및 기능 : <see cref="HistorianIntegrityChecker.CheckAndRepairAsync"/>의 결과 3가지를 나타내는 값
/// </summary>
public enum HistorianIntegrityOutcome
{
    /// <summary>손상이 감지되지 않음 — 아무 조치도 하지 않음.</summary>
    Ok,

    /// <summary>손상이 감지됐고 최신 백업으로 자동 복원됨.</summary>
    RestoredFromBackup,

    /// <summary>손상이 감지됐지만 사용 가능한 백업이 없어 빈 DB로 재초기화됨.</summary>
    ReinitializedEmpty,
}
