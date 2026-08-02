namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 와이어(연결선)
/// 역활 및 기능 : 캔버스에서 두 노드를 잇는 연결선 하나를 나타내는 모델
///
/// 캔버스에서 두 노드를 잇는 연결선 하나를 나타냅니다. Node-RED <c>flows.json</c>의
/// <c>wires</c> 배열 항목 하나에 대응합니다.
/// 설계 근거: 02번 문서 2번 탭 카드 2.
/// </summary>
/// <remarks>
/// <c>record</c>이므로 값 기반 동등성을 가지며 불변입니다 — 연결선을 바꾸려면 항상 새
/// <see cref="Wire"/> 인스턴스를 만들어 교체합니다(끊기/새로 잇기와 동일한 개념).
/// </remarks>
/// <param name="SourceNodeId">이 연결선이 시작되는 노드의 <see cref="NodeConfig.Id"/>.</param>
/// <param name="SourcePort">시작 노드의 몇 번째 출력 포트에서 나가는지(0부터 시작). 여러 출력 포트를 가진 노드를 지원하기 위한 필드입니다.</param>
/// <param name="TargetNodeId">이 연결선이 도착하는 노드의 <see cref="NodeConfig.Id"/>.</param>
/// <param name="TargetPort">도착 노드의 몇 번째 입력 포트로 들어가는지(0부터 시작).</param>
/// <example>
/// <code>
/// // Inject 노드(n1)의 0번 출력 포트 → Function 노드(n2)의 0번 입력 포트
/// var wire = new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0);
///
/// // Switch 노드(n2)가 3개 출력 포트를 가질 때, 각 포트를 서로 다른 노드로 연결
/// var wires = new List&lt;Wire&gt;
/// {
///     new(SourceNodeId: "n2", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0), // 조건1 → 알람 노드
///     new(SourceNodeId: "n2", SourcePort: 1, TargetNodeId: "n4", TargetPort: 0), // 조건2 → 로그 노드
///     new(SourceNodeId: "n2", SourcePort: 2, TargetNodeId: "n5", TargetPort: 0), // 그 외 → Debug 노드
/// };
/// </code>
/// </example>
public sealed record Wire(string SourceNodeId, int SourcePort, string TargetNodeId, int TargetPort);
