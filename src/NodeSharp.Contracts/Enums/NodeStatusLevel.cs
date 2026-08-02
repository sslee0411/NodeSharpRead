namespace NodeSharp.Contracts.Enums;

// 한글명: 노드 상태 레벨
/// <summary>
/// 노드 하단 상태 점(status dot)의 색상을 타입 세이프하게 표현하는 5단계 팔레트입니다.
/// Node-RED의 <c>node.status({fill, shape, text})</c> API에서 <c>fill</c>에 자유 문자열
/// ("red"/"green"/"yellow"/"blue"/"grey")을 넘기던 방식과 호환되도록, 각 멤버 이름이 그
/// 문자열과 1:1 대응합니다(대소문자만 다름).
/// 설계 근거: 02번 문서 7번 탭 카드 2.
/// </summary>
/// <remarks>
/// <c>NodeStatusEvent.Fill</c> 필드는 Node-RED 원문 호환·SignalR 페이로드 형식 유지를 위해
/// 여전히 <see cref="string"/>입니다. 이 Enum은 그 필드를 대체하는 것이 아니라, 노드 개발자가
/// 오타 없이 쓸 수 있는 타입 세이프 편의 계층입니다 — <c>NodeContext.SetStatus(NodeStatusLevel, ...)</c>
/// 오버로드가 내부적으로 문자열로 변환해 기존 <c>SetStatus(string, string, string)</c>에
/// 위임하므로, 이미 정의된 이벤트 필드·전송 포맷은 전혀 바뀌지 않습니다.
/// </remarks>
/// <example>
/// <code>
/// // 기존 방식(Node-RED와 동일한 자유 문자열, 오타 위험 있음)
/// ctx.SetStatus("green", "dot", "연결됨");
///
/// // 신규 방식 — 컴파일 시점에 오타를 잡아줌
/// ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");
///
/// // PLC 통신 흐름 전체에서 단계별로 상태 갱신
/// ctx.SetStatus(NodeStatusLevel.Blue, "dot", "연결 시도 중...");
/// try
/// {
///     await plcClient.ConnectAsync();
///     ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");
/// }
/// catch (Exception ex)
/// {
///     ctx.SetStatus(NodeStatusLevel.Red, "dot", $"연결 실패: {ex.Message}");
/// }
/// </code>
/// </example>
public enum NodeStatusLevel
{
    /// <summary>대기·미설정 상태. Node-RED fill "grey"에 대응합니다(예: 아직 배포되지 않음).</summary>
    Grey,

    /// <summary>진행 중이거나 연결을 시도하는 등 정보성 상태. Node-RED fill "blue"에 대응합니다.</summary>
    Blue,

    /// <summary>정상 동작 중인 상태. Node-RED fill "green"에 대응합니다(예: PLC 연결됨).</summary>
    Green,

    /// <summary>주의가 필요하지만 치명적이지는 않은 경고 상태. Node-RED fill "yellow"에 대응합니다.</summary>
    Yellow,

    /// <summary>오류 상태. Node-RED fill "red"에 대응합니다(예: 통신 실패, 값 범위 초과).</summary>
    Red
}
