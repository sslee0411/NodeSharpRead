namespace NodeSharp.Contracts.Models;

// 한글명: 프로젝트 번들
/// <summary>
/// 프로젝트 번들(<c>.nsproj</c>, zip)이 실제로 포함하는 저장 파일 목록을 나타내는 순수 데이터
/// 레코드입니다. 개발 PC에서 만든 설정을 운영 PC로 옮길 때 flows.json·device.json 등 여러
/// 파일을 하나씩 옮기면 실수하기 쉬워, 이 목록 기준으로 압축 파일 하나로 묶습니다. 실제
/// 압축/해제(zip I/O)는 이 레코드를 참조하는 <c>ProjectBundleExporter</c>(<c>OP-08</c>)가 담당합니다.
/// 설계 근거: 02번 문서 10번 탭 카드 11.
/// </summary>
/// <remarks>
/// 파일 목록의 출처를 이 레코드 하나로 유지합니다 — 새 저장 파일이 추가되면 <see cref="DefaultIncludedFileNames"/>만
/// 갱신하면 되고, 압축 로직(<c>OP-08</c>) 쪽 코드는 건드릴 필요가 없습니다.
/// </remarks>
/// <param name="IncludedFileNames">프로젝트 번들에 포함되는 파일 이름 목록(저장소 루트 기준 상대 경로).</param>
/// <param name="ExcludedFileName">번들에서 의도적으로 제외되는 파일 이름. 기본값은 <c>"credentials.json"</c>입니다(DPAPI로 머신 바인딩되어 있어 다른 PC로 옮기면 복호화가 실패하므로, 가져오기 시 사용자가 재입력하도록 유도).</param>
/// <example>
/// <code>
/// // 1) 기본 구성 — 확정된 7개 파일 + credentials.json 자동 제외
/// var bundle = ProjectBundle.Default;
/// bool hasFlows = bundle.IncludedFileNames.Contains("flows.json"); // true
/// bool credsExcluded = bundle.ExcludedFileName == "credentials.json"; // true
///
/// // 2) OP-08 내보내기 로직에서의 사용 예 — 목록에 있는 파일만 zip에 담고 제외 파일은 건너뜀
/// foreach (var fileName in bundle.IncludedFileNames)
/// {
///     // zipArchive.CreateEntryFromFile(Path.Combine(projectDir, fileName), fileName);
/// }
///
/// // 3) 필요 시 파일 목록을 커스텀해 새 번들을 만들 수도 있음(record with — 원본은 불변 유지)
/// var minimalBundle = bundle with { IncludedFileNames = new[] { "flows.json", "device.json" } };
/// </code>
/// </example>
public sealed record ProjectBundle(
    IReadOnlyList<string> IncludedFileNames,
    string ExcludedFileName = "credentials.json")
{
    /// <summary>
    /// 10번 탭 카드 11이 정의한 7개 저장 파일 — flows.json(3번 탭)·device.json(8번 탭)·
    /// scale-library.json·alarm-library.json·comm-library.json(모두 8번 탭)·sequences.json(11번 탭)·
    /// dashboard.json(9번 탭). <c>OP-08</c>이 이 목록을 그대로 사용합니다.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultIncludedFileNames = new[]
    {
        "flows.json",
        "device.json",
        "scale-library.json",
        "alarm-library.json",
        "comm-library.json",
        "sequences.json",
        "dashboard.json"
    };

    /// <summary><see cref="DefaultIncludedFileNames"/>(현재 확정된 7개 파일)를 사용하는 기본 번들 구성입니다.</summary>
    public static ProjectBundle Default => new(DefaultIncludedFileNames);
}
