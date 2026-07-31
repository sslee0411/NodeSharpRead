using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// 값의 "출처"(<see cref="Source"/>)와 "실제 값/경로"(<see cref="Value"/>)를 함께 담는 모델입니다.
/// <c>PropertyFieldType.TypedValue</c> 필드의 저장 값 타입이며, Change/Range/Switch 노드처럼 값을
/// 고정 문자열이 아니라 msg 필드·Context·환경변수·수식 중에서 선택해야 하는 경우에 재사용합니다.
/// 설계 근거: 02번 문서 9번 탭 카드 3(v1.10 신설).
/// </summary>
/// <remarks>
/// <see cref="Value"/>의 해석 방식은 <see cref="Source"/>에 따라 달라집니다: <see cref="TypedValueSource.Fixed"/>는
/// 리터럴 값 그대로, <see cref="TypedValueSource.MsgField"/>는 <c>"payload.temp"</c>처럼 msg 하위 경로,
/// <see cref="TypedValueSource.FlowContext"/>/<see cref="TypedValueSource.GlobalContext"/>는 Context 키(<c>RT-09a</c>),
/// <see cref="TypedValueSource.EnvVar"/>는 환경변수 이름(<c>NR-10b</c>), <see cref="TypedValueSource.Expression"/>은
/// NCalc 수식(<c>FN-01</c> 실행기 재사용)입니다.
/// </remarks>
/// <example>
/// <code>
/// // Switch 노드의 비교 대상 값을 msg 필드로 지정
/// var compareTo = new TypedValue(TypedValueSource.MsgField, "payload.threshold");
///
/// // NodeConfig 저장/재로드 후에도 Source·Value가 그대로 복원되는지가 완료 기준
/// var json = JsonSerializer.Serialize(compareTo);
/// var restored = JsonSerializer.Deserialize&lt;TypedValue&gt;(json);
/// // restored.Source == TypedValueSource.MsgField, restored.Value == "payload.threshold"
/// </code>
/// </example>
public sealed record TypedValue(TypedValueSource Source, string Value);
