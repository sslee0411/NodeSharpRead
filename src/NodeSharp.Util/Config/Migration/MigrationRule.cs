using Newtonsoft.Json.Linq;

namespace NodeSharp.Util.Config.Migration;

// 한글명: 마이그레이션 규칙
/// <summary>
/// <c>flows.json</c>을 한 스키마 버전에서 바로 다음 버전으로 딱 한 단계만 변환하는 규칙입니다
/// (lssLib.Config의 <c>Migration/MigrationRule.cs</c> 대응 — RT-11 summary 참고: 실제 lssLib.Config
/// 소스를 dev-csharp 스킬 문서·GitHub 저장소 어디서도 확인할 수 없어, 02번 문서 3번 탭 카드7 의사코드
/// 의도만 그대로 살려 직접 설계했습니다). 여러 버전을 건너뛰는 마이그레이션은 이런 규칙 여러 개를
/// <see cref="NodeSharp.Util.Config.Migration.ConfigMigration"/>이 순서대로 이어 붙여 처리합니다 —
/// 규칙 하나는 항상 "버전 N → 버전 N+1" 한 단계만 책임집니다(누적 변환 로직을 여러 규칙에 나눠
/// 갖고 있지 않아도 되게 하려는 의도).
/// </summary>
/// <param name="FromVersion">이 규칙이 적용되는 원본 스키마 버전.</param>
/// <param name="ToVersion">변환 후 스키마 버전(보통 <see cref="FromVersion"/>+1).</param>
/// <param name="Transform">원본 JSON(<see cref="JObject"/>)을 <see cref="ToVersion"/> 스키마로
/// 바꾸는 변환 함수 — 원본을 직접 수정하지 않고 새 <see cref="JObject"/>를 반환하는 것을 권장합니다.</param>
/// <example>
/// <code>
/// // v1의 "wires": [[0,1]] (노드 배열 인덱스 쌍) 형태를 v2의 Wire 레코드 배열로 변환하는 규칙 예시
/// var v1ToV2 = new MigrationRule(FromVersion: 1, ToVersion: 2, Transform: v1 =&gt;
/// {
///     var nodes = (JArray)v1["nodes"]!;
///     var wires = (JArray)v1["wires"]!;
///     var wireRecords = new JArray();
///     foreach (var pair in wires)
///     {
///         var fromIdx = (int)pair[0]!;
///         var toIdx = (int)pair[1]!;
///         wireRecords.Add(new JObject
///         {
///             ["SourceNodeId"] = nodes[fromIdx]!["id"],
///             ["SourcePort"] = 0,
///             ["TargetNodeId"] = nodes[toIdx]!["id"],
///             ["TargetPort"] = 0,
///         });
///     }
///     var v2 = (JObject)v1.DeepClone();
///     v2["Wires"] = wireRecords;
///     return v2;
/// });
/// </code>
/// </example>
public sealed record MigrationRule(int FromVersion, int ToVersion, Func<JObject, JObject> Transform);
