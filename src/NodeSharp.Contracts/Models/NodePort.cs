namespace NodeSharp.Contracts.Models;

// 한글명: 노드 포트
/// <summary>
/// 노드 하나가 가질 수 있는 입력 또는 출력 포트 하나를 나타냅니다. 대부분의 Node-RED 노드는
/// 입력 1개·출력 1개(또는 0개)뿐이지만, Switch 노드처럼 조건별로 여러 출력 포트를 갖는
/// 노드를 지원하기 위해 포트를 독립된 모델로 분리했습니다(iiot-system-arch의 N개 입/출력
/// 포트 지원 패턴과 동일).
/// 설계 근거: 02번 문서 2번 탭 카드 2.
/// </summary>
/// <param name="Index">이 노드 안에서의 포트 순서(0부터 시작). <see cref="Wire.SourcePort"/>/<see cref="Wire.TargetPort"/>가 가리키는 값이 이 순서입니다.</param>
/// <param name="Label">포트 옆에 표시되는 짧은 이름(예: Switch 노드의 각 출력 포트에 표시되는 조건 요약).</param>
/// <example>
/// <code>
/// // Switch 노드가 조건 2개 + 기본(그 외) 1개, 총 3개 출력 포트를 갖는 예
/// var ports = new[]
/// {
///     new NodePort(Index: 0, Label: "온도 높음"),
///     new NodePort(Index: 1, Label: "온도 낮음"),
///     new NodePort(Index: 2, Label: "그 외"),
/// };
///
/// // Wire.SourcePort/TargetPort는 이 Index 값을 그대로 참조한다 — 0번 포트(온도 높음)를 알람 노드로 연결
/// var wire = new Wire(SourceNodeId: "switch-1", SourcePort: ports[0].Index, TargetNodeId: "alarm-1", TargetPort: 0);
/// </code>
/// </example>
public sealed record NodePort(int Index, string Label);
