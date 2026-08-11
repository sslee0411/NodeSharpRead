using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Switch;

/// <summary>
/// Class명 : Switch 규칙
/// 역활 및 기능 : Switch 노드의 조건 1개(연산자 + 비교값 1~2개)를 나타내는 모델
///
/// Switch 노드(NR-04)가 갖는 여러 조건 중 하나를 나타냅니다. 규칙 목록에서 이 레코드가 있는 순서가
/// 곧 <see cref="SwitchNode.OutputPorts"/>의 포트 순서입니다(규칙 0번째 → 0번 출력 포트, 1번째 → 1번
/// 출력 포트, ...). Node-RED의 <c>10-switch.js</c> 규칙(<c>{t, v, vt, v2, v2t, case}</c>)과 같은
/// 개념이지만, 비교값은 자체 문자열 대신 이미 있는 <see cref="TypedValue"/>(CT-08)를 그대로 재사용해
/// 고정값뿐 아니라 msg 필드·Flow/Global Context·수식으로도 지정할 수 있습니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Operator"/>는 Node-RED와 동일한 짧은 코드를 그대로 씁니다: <c>eq</c>/<c>neq</c>/
/// <c>lt</c>/<c>lte</c>/<c>gt</c>/<c>gte</c>/<c>btwn</c>/<c>cont</c>/<c>regex</c>/<c>true</c>/
/// <c>false</c>/<c>null</c>/<c>nnull</c>/<c>empty</c>/<c>nempty</c>/<c>istype</c>/<c>else</c> —
/// 21종 중 <c>head</c>/<c>tail</c>/<c>index</c>(msg.parts 시퀀스 처리, <c>NR-13a</c>/<c>NR-13b</c>가
/// 완료된 뒤 이연 지원 예정)와 <c>jsonata_exp</c>(JSONata 엔진 필요, 아직 이 프로젝트에 없어 별도
/// Step 신설 필요)는 이 Step(NR-04) 범위에서 제외했습니다(사용자 확인, 2026-08 세션).</item>
/// <item><see cref="CompareValue"/>는 <c>true</c>/<c>false</c>/<c>null</c>/<c>nnull</c>/<c>empty</c>/
/// <c>nempty</c>/<c>else</c>처럼 비교 대상 값이 필요 없는 연산자에서는 <c>null</c>로 둡니다.</item>
/// <item><see cref="CompareValue2"/>는 <c>btwn</c>(구간) 연산자에서만 사용합니다.</item>
/// <item>이 레코드는 <c>System.Text.Json</c>으로 직접 직렬화/역직렬화됩니다 — <c>PropertyField</c>에
/// "규칙 목록"을 담을 전용 필드 타입이 아직 없어(<c>PropertyFieldType</c> 확인 결과 리스트/반복 그룹
/// 타입 없음), <see cref="SwitchNodeType"/>이 이 레코드의 목록을 JSON 배열 문자열 하나로 인코딩해
/// <c>PropertyFieldType.Code</c> 필드 하나에 저장합니다(임시 패턴 — 향후 반복 그룹 필드 타입이
/// 생기면 Change(NR-12a)와 함께 정식 UI로 교체 예정).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // payload가 85 이상이면 0번 포트, 그 외에는 1번 포트로 라우팅하는 규칙 2개
/// var rules = new[]
/// {
///     new SwitchRule("gte", CompareValue: new TypedValue(TypedValueSource.Fixed, "85")),
///     new SwitchRule("else"),
/// };
///
/// // btwn은 CompareValue/CompareValue2 둘 다 사용
/// var between = new SwitchRule("btwn",
///     CompareValue: new TypedValue(TypedValueSource.Fixed, "10"),
///     CompareValue2: new TypedValue(TypedValueSource.Fixed, "20"));
/// </code>
/// </example>
public sealed record SwitchRule(
    string Operator,
    TypedValue? CompareValue = null,
    TypedValue? CompareValue2 = null,
    bool CaseSensitive = false);
