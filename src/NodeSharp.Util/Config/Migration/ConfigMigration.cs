using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Util.Config.Migration;

// 한글명: 설정 마이그레이션
/// <summary>
/// <c>flows.json</c>/<c>device.json</c> 같은 설정 파일이 옛 스키마 버전으로 저장돼 있을 때, 등록된
/// <see cref="MigrationRule"/>들을 순서대로 이어 붙여 최신 버전으로 자동 변환합니다(RT-11, 02번 문서
/// 3번 탭 카드7 "설정 파일 스키마 마이그레이션(lssLib.Config 재사용)" 원본 — <c>lssLib.Config</c>의
/// <c>Migration/ConfigMigration.cs</c> 대응). 실제 lssLib.Config 소스는 dev-csharp 스킬 문서에도
/// 없고(Config 모듈 자체가 스킬 범위 밖) GitHub 저장소(sslee0411/lssLib)도 이 개발 환경에서 접근할 수
/// 없어(RT-09c의 "JsonWriteService" 때와 같은 유형의 공백, 사용자 확인: "직접 설계로 구현") 원본과
/// 이름을 맞춘 시그니처를 그대로 포팅하는 대신, 카드7 의사코드(<c>ConfigMigration.Apply(rawJson,
/// fromVersion, toVersion)</c>, <c>BackupOriginal(path)</c>)의 동작 의도만 살려 직접 설계했습니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>인스턴스 vs 싱글턴</b>: <c>NodeSharp.Util.Messaging.EventBus</c>·<c>NodeSharp.Util.Messaging.AsyncScheduler</c>
/// (RT-07/RT-08)와 동일하게 보통은 <see cref="Instance"/>(앱 전체 공유)를 쓰고, 테스트처럼 독립된 규칙 집합이 필요할
/// 때는 생성자를 <c>public</c>으로 열어 <c>new ConfigMigration()</c>으로 별도 인스턴스를 만들 수
/// 있게 했습니다(v1.91에서 <c>EventBus</c> 생성자를 <c>private</c>로 막았다가 테스트가 막혀 겪은
/// <c>CS0122</c> 문제를 이번엔 처음부터 피하려는 선제 조치).</item>
/// <item><b>규칙이 없는 구간</b>: <see cref="Apply"/>가 현재 버전에서 다음 버전으로 넘어갈 규칙을
/// 찾지 못하면 <see cref="InvalidOperationException"/>을 던집니다 — 조용히 원본을 그대로 반환하면
/// "마이그레이션에 성공했지만 실제로는 옛 스키마 그대로"인 상태를 완료로 착각할 수 있기 때문입니다.</item>
/// <item><b>버전 1은 아직 실사용된 적 없는 예시</b>: <see cref="FlowFileHeader.CurrentSchemaVersion"/>
/// XML 주석 참고 — 이 Step 이전에는 스키마 버전 관리 자체가 없어, 실제로 배포된 적 있는 "구버전"은
/// 존재하지 않습니다. 이 클래스와 <see cref="MigrationRule"/>은 향후 실제 스키마 변경이 생겼을 때
/// 쓸 재사용 가능한 뼈대이고, 지금 당장의 v1→v2 규칙은 테스트에서만 예시로 등록합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var migration = new ConfigMigration();
/// migration.RegisterRule(new MigrationRule(1, 2, v1 =&gt; { /* ... */ return v1; }));
///
/// string rawJson = File.ReadAllText("flows.json");
/// var header = new FlowFileHeader(SchemaVersion: 1, SavedAt: DateTime.Now);
/// string migrated = migration.MigrateIfNeeded(rawJson, header, backupPath: "flows.json");
/// // migrated는 이제 FlowFileHeader.CurrentSchemaVersion 스키마 — FlowDefinition으로 역직렬화해
/// // FlowEngine.DeployAsync에 그대로 넘길 수 있다(완료 기준: "정상 배포까지 이어지는지").
/// </code>
/// </example>
public sealed class ConfigMigration
{
    private readonly List<MigrationRule> _rules = new();

    /// <summary>앱 전체가 공유하는 기본 인스턴스입니다. 보통은 이것을 씁니다.</summary>
    public static ConfigMigration Instance { get; } = new();

    /// <summary>
    /// 보통은 <see cref="Instance"/>를 쓰고, 테스트처럼 다른 코드와 규칙 목록이 섞이면 안 되는
    /// 독립된 인스턴스가 필요할 때만 이 생성자로 직접 만듭니다.
    /// </summary>
    public ConfigMigration()
    {
    }

    /// <summary>이 인스턴스에 마이그레이션 규칙 하나를 등록합니다. 같은 <see cref="MigrationRule.FromVersion"/>을 여러 번 등록하면 먼저 등록한 규칙이 우선합니다.</summary>
    public void RegisterRule(MigrationRule rule) => _rules.Add(rule);

    /// <summary>
    /// <paramref name="rawJson"/>을 <paramref name="fromVersion"/>에서 <paramref name="toVersion"/>까지
    /// 등록된 규칙을 순서대로 적용해 변환합니다. 이미 같은 버전이면(<paramref name="fromVersion"/> ==
    /// <paramref name="toVersion"/>) 원본을 그대로 반환합니다.
    /// </summary>
    /// <exception cref="InvalidOperationException">중간에 다음 버전으로 넘어갈 규칙을 찾지 못하면 던집니다.</exception>
    public string Apply(string rawJson, int fromVersion, int toVersion)
    {
        if (fromVersion == toVersion)
        {
            return rawJson;
        }

        var current = JObject.Parse(rawJson);
        var version = fromVersion;
        while (version < toVersion)
        {
            var rule = _rules.FirstOrDefault(r => r.FromVersion == version);
            if (rule is null)
            {
                throw new InvalidOperationException(
                    $"스키마 버전 {version}에서 다음 단계로 넘어갈 마이그레이션 규칙이 등록돼 있지 않습니다(목표 버전: {toVersion}).");
            }

            current = rule.Transform(current);
            version = rule.ToVersion;
        }

        return current.ToString(Formatting.Indented);
    }

    /// <summary>
    /// 02번 문서 카드7 의사코드의 오케스트레이션 전체(헤더 버전 확인 → 필요하면 백업 → 마이그레이션)를
    /// 한 번에 수행합니다. <paramref name="header"/>가 이미 최신 버전이면(<see cref="FlowFileHeader.SchemaVersion"/>
    /// &gt;= <see cref="FlowFileHeader.CurrentSchemaVersion"/>) 아무 작업도 하지 않고 원본을 그대로 반환합니다.
    /// </summary>
    /// <param name="rawJson">파일에서 읽은 원본 JSON 문자열.</param>
    /// <param name="header">그 파일의 <see cref="FlowFileHeader"/>(스키마 버전 확인용).</param>
    /// <param name="backupPath">마이그레이션이 실제로 필요할 때 <see cref="BackupOriginal"/>로 백업할
    /// 원본 파일 경로. <c>null</c>이면 백업을 건너뜁니다(예: 파일이 아닌 메모리 상 JSON을 다룰 때).</param>
    public string MigrateIfNeeded(string rawJson, FlowFileHeader header, string? backupPath = null)
    {
        if (header.SchemaVersion >= FlowFileHeader.CurrentSchemaVersion)
        {
            return rawJson;
        }

        if (backupPath is not null)
        {
            BackupOriginal(backupPath);
        }

        return Apply(rawJson, header.SchemaVersion, FlowFileHeader.CurrentSchemaVersion);
    }

    /// <summary>
    /// 마이그레이션 전 원본 파일을 같은 폴더에 타임스탬프가 붙은 이름으로 복사해둡니다(02번 문서
    /// 카드7의 <c>BackupOriginal(path)</c>, 실제 백업 정책 상세는 11번 탭 참고). 파일이 없으면
    /// 조용히 아무 일도 하지 않습니다(메모리 상 JSON만 다루는 호출부를 배려).
    /// </summary>
    public void BackupOriginal(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = $"{path}.bak.{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(path, backupPath, overwrite: false);
    }
}
