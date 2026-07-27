namespace NodeSharp.Contracts.Enums;

/// <summary>
/// 노드 하단 상태 점(status dot)의 색상을 타입 세이프하게 표현하는 5단계 팔레트입니다.
/// Node-RED의 <c>node.status({fill, shape, text})</c> API에서 <c>fill</c>에 자유롭게
/// 문자열("red"/"green"/"yellow"/"blue"/"grey")을 넘기던 방식과 100% 호환되도록,
/// 이 Enum의 각 멤버 이름은 그 문자열과 1:1 대응합니다(대소문자만 다름).
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서(<c>docs/02_CSharp_구조설계.html</c>) 7번 탭(실시간 모니터링·디버깅·보안)
/// 카드 2 — <c>NodeStatusEvent(string NodeId, string Fill, string Shape, string Text, DateTime At)</c>
/// 레코드의 <c>Fill</c> 필드는 Node-RED 원문과의 호환성 및 SignalR JSON 페이로드 형식을 그대로
/// 유지하기 위해 여전히 <see cref="string"/>입니다. 이 Enum은 그 문자열 필드를 대체하는 것이
/// 아니라, <b>노드 개발자가 오타 없이 쓸 수 있는 타입 세이프 편의 계층</b>으로 추가되었습니다
/// (README.md Ver History v1.45, 02번 문서 v1.38에서 근거를 먼저 기록한 뒤 이 코드를 작성함).
/// </para>
/// <para>
/// 실제 변환은 <c>NodeContext.SetStatus(NodeStatusLevel, string, string)</c> 오버로드(CT-05a에서
/// 구현)가 담당하며, 내부적으로 <c>level.ToString().ToLowerInvariant()</c>로 변환해 기존
/// <c>SetStatus(string fill, string shape, string text)</c> 오버로드에 위임합니다. 즉 이 Enum이
/// 추가되어도 이미 정의된 <c>NodeStatusEvent</c>의 필드나 Runner→Editor SignalR 전송 포맷은
/// 전혀 바뀌지 않습니다.
/// </para>
/// </remarks>
/// <example>
/// 노드 구현 코드에서 상태를 표시할 때(향후 Function/PLC 노드 등에서 실제로 사용):
/// <code>
/// // 기존 방식(Node-RED와 동일한 자유 문자열, 오타 위험 있음)
/// ctx.SetStatus("green", "dot", "연결됨");
///
/// // 신규 방식(NodeStatusLevel — 컴파일 시점에 오타를 잡아줌)
/// ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");
///
/// // PLC 통신 실패 예시(ED-D06a PLC Write 안전장치와 함께 사용)
/// if (!writeSucceeded)
/// {
///     ctx.SetStatus(NodeStatusLevel.Red, "dot", $"쓰기 실패: {tagName}");
/// }
/// </code>
/// </example>
public enum NodeStatusLevel
{
    /// <summary>대기·미설정 상태. Node-RED fill "grey"에 대응합니다. (예: 노드가 아직 배포되지 않음)</summary>
    Grey,

    /// <summary>진행 중이거나 연결을 시도하는 등 정보성 상태. Node-RED fill "blue"에 대응합니다.</summary>
    Blue,

    /// <summary>정상 동작 중인 상태. Node-RED fill "green"에 대응합니다. (예: PLC 연결됨, 폴링 정상)</summary>
    Green,

    /// <summary>주의가 필요하지만 치명적이지는 않은 경고 상태. Node-RED fill "yellow"에 대응합니다.</summary>
    Yellow,

    /// <summary>오류 상태. Node-RED fill "red"에 대응합니다. (예: 통신 실패, 값 범위 초과)</summary>
    Red
}
