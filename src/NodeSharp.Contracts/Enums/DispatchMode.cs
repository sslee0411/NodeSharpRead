namespace NodeSharp.Contracts.Enums;

// 한글명: 출력 전달 모드
/// <summary>
/// 한 노드가 여러 출력 와이어를 가질 때(Fan-out) 메시지를 전달하는 순서를 지정합니다.
/// <see cref="Models.NodeConfig.OutputDispatch"/>의 타입이며, 노드가 하나의 입력을 받아
/// 여러 다음 노드로 갈라 보낼 때 이 값에 따라 실행 방식이 달라집니다.
/// 설계 근거: 02번 문서 5번 탭 카드 1.
/// </summary>
/// <example>
/// <code>
/// // 기본값(Sequential) — 로그 기록 → DB 저장 순서가 보장돼야 하는 노드
/// var logThenSave = new NodeConfig(Id: "n1", Type: "function", Name: "로그+저장", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt;(), OutputDispatch: DispatchMode.Sequential);
///
/// // Parallel — 3개 출력 와이어(알람 발행, Dashboard 갱신, Historian 기록)를 동시에 실행해 지연을 줄임
/// var fanOutAlarm = new NodeConfig(Id: "n2", Type: "function", Name: "알람 분배", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt;(), OutputDispatch: DispatchMode.Parallel);
/// </code>
/// </example>
public enum DispatchMode
{
    /// <summary>순차 전달 — 첫 번째 와이어의 처리가 끝난 뒤 다음 와이어로 전달합니다(기본값). 분기 간 실행 순서가 보장됩니다.</summary>
    Sequential,

    /// <summary>병렬 전달 — 모든 와이어에 동시에 전달합니다. 처리량은 높지만 분기 간 실행 순서는 보장되지 않습니다.</summary>
    Parallel
}
