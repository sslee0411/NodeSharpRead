namespace NodeSharp.Contracts.Models;

/// <summary>
/// 노드 하나가 가질 수 있는 입력 또는 출력 포트 하나를 나타냅니다. 대부분의 Node-RED 노드는
/// 입력 1개·출력 1개(또는 0개)뿐이지만, Switch 노드처럼 조건별로 여러 출력 포트를 갖는
/// 노드를 지원하기 위해 포트를 독립된 모델로 분리했습니다.
/// </summary>
/// <remarks>
/// 설계 근거: 02번 설계 문서 2번 탭 카드 2 — "N개 입/출력 포트 지원(iiot-system-arch S-20 패턴)"으로
/// 명시된 모델입니다. <see cref="Wire.SourcePort"/>/<see cref="Wire.TargetPort"/>가 가리키는
/// <see cref="Index"/> 값이 바로 이 포트의 순서입니다.
/// </remarks>
/// <param name="Index">이 노드 안에서의 포트 순서(0부터 시작). 캔버스에서 위→아래로 나열되는 순서와 같습니다.</param>
/// <param name="Label">포트 옆에 표시되는 짧은 이름(예: Switch 노드의 각 출력 포트에 표시되는 조건 요약).</param>
/// <example>
/// <code>
/// // Switch 노드가 조건 2개 + 기본(그 외) 1개, 총 3개 출력 포트를 갖는 예
/// var ports = new[]
/// {
///     new NodePort(Index: 0, Label: "온도 > 80"),
///     new NodePort(Index: 1, Label: "온도 <= 80"),
///     new NodePort(Index: 2, Label: "그 외"),
/// };
/// </code>
/// </example>
public sealed record NodePort(int Index, string Label);
