namespace NodeSharp.Contracts.Models;

// 한글명: 플러그인 매니페스트
/// <summary>
/// <c>nodes/*.dll</c> 플러그인이 자신을 설명하기 위해 포함하는 매니페스트입니다. 노드 플러그인
/// 프로젝트는 Contracts만 참조하므로(1번 탭 솔루션 구조), 이 레코드는 Contracts에 있어야 플러그인
/// 쪽에서 직접 생성할 수 있습니다 — <c>PluginLoadContext</c>/<c>NodeTypeRegistry</c>(Registry 소속,
/// <c>CT-06a</c>/<c>CT-06b</c>)와 달리 이 레코드는 순수 데이터라 Contracts 배치가 자연스럽습니다.
/// 설계 근거: 02번 문서 10번 탭 카드 8(플러그인 버전 호환성 검사).
/// </summary>
/// <param name="TypeName">이 플러그인이 제공하는 노드 타입 이름(예: <c>"inject"</c>, <c>"http-request"</c>).</param>
/// <param name="PluginVersion">플러그인 자체의 버전(참고용 — 호환성 판단에는 쓰이지 않음).</param>
/// <param name="RequiredContractsVersion">이 플러그인이 요구하는 <c>NodeSharp.Contracts</c> 버전(SemVer 형식, 예: <c>"1.2.0"</c>). <c>NodeTypeRegistry</c>가 로드 시 현재 Contracts 버전과 비교합니다.</param>
/// <example>
/// <code>
/// // Inject 노드 플러그인이 자신의 dll에 포함하는 매니페스트
/// var manifest = new PluginManifest(TypeName: "inject", PluginVersion: "1.0.0", RequiredContractsVersion: "1.0.0");
///
/// // NodeTypeRegistry가 로드 전 SemVer 호환성 체크(주 버전이 다르면 로드 거부)
/// if (!SemVer.IsCompatible(manifest.RequiredContractsVersion, currentContractsVersion))
///     // 크래시 대신 이 플러그인만 제외하고 계속 진행(경고 로그)
/// </code>
/// </example>
public sealed record PluginManifest(string TypeName, string PluginVersion, string RequiredContractsVersion);
