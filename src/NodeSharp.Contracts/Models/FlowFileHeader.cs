namespace NodeSharp.Contracts.Models;

// 한글명: 플로우 파일 헤더
/// <summary>
/// <c>flows.json</c> 파일 맨 앞에 저장되는 작은 머리말입니다(RT-11, 02번 문서 3번 탭 카드7 원본 —
/// <c>FlowFileHeader(int SchemaVersion, DateTime SavedAt)</c> 그대로). 파일 안의 실제
/// <see cref="FlowDefinition"/> 목록을 읽기 전에 이 헤더만 먼저 읽어, 이 파일이 몇 번째 스키마
/// 버전으로 저장됐는지(<see cref="SchemaVersion"/>)를 빠르게 확인하는 용도입니다. 저장된 버전이
/// <see cref="CurrentSchemaVersion"/>보다 작으면 옛 버전 파일이라는 뜻이고, 실제 변환은
/// <c>NodeSharp.Util.Config.Migration.ConfigMigration</c>(RT-11)이 담당합니다.
/// </summary>
/// <param name="SchemaVersion">이 파일이 저장될 당시의 스키마 버전.</param>
/// <param name="SavedAt">이 파일이 마지막으로 저장된 시각(로컬 시간).</param>
/// <example>
/// <code>
/// var header = new FlowFileHeader(SchemaVersion: 1, SavedAt: DateTime.Now);
/// if (header.SchemaVersion &lt; FlowFileHeader.CurrentSchemaVersion)
/// {
///     // 구버전 파일 — ConfigMigration.Apply/MigrateIfNeeded로 최신 스키마로 변환 필요
/// }
/// </code>
/// </example>
public sealed record FlowFileHeader(int SchemaVersion, DateTime SavedAt)
{
    /// <summary>
    /// 지금 실행 중인 코드가 이해하는 최신 <c>flows.json</c> 스키마 버전입니다. 이 상수를 도입하는
    /// RT-11 시점을 버전 2로 정했습니다(<see cref="NodeConfig"/>/<see cref="Wire"/>/
    /// <see cref="FlowDefinition"/>의 지금 형태가 버전 2에 해당). 버전 1은 이 프로젝트가 실제로
    /// 배포한 적이 없는 가상의 구버전 예시입니다 — RT-11 이전에는 스키마 버전 관리 자체가 없었기
    /// 때문입니다(테스트에서만 "레거시 인덱스 기반 wires 배열" 예시로 사용, RT-11 summary 참고).
    /// 이후 실제로 <c>flows.json</c> 구조가 바뀌는 Step이 생기면 이 값을 올리고 새 마이그레이션
    /// 규칙을 등록합니다.
    /// </summary>
    public const int CurrentSchemaVersion = 2;
}
