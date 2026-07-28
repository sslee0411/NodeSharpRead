namespace NodeSharp.Contracts.Models;

/// <summary>
/// 캔버스에서 두 노드를 잇는 연결선 하나를 나타냅니다. Node-RED <c>flows.json</c>의
/// <c>wires</c> 배열 항목 하나에 대응합니다.
/// </summary>
/// <remarks>
/// 설계 근거: 02번 설계 문서 2번 탭 카드 2. <c>record</c>로 선언되어 있어 값 기반 동등성
/// (<c>Equals</c>/<c>==</c>가 모든 필드를 비교)을 기본으로 가지며, 불변(모든 필드가 <c>init</c>)입니다
/// — 연결선을 "변경"하려면 항상 새 <see cref="Wire"/> 인스턴스를 만들어 교체합니다(끊기/새로
/// 잇기와 동일한 개념이라 자연스러운 설계).
/// </remarks>
/// <param name="SourceNodeId">이 연결선이 시작되는 노드의 <see cref="NodeConfig.Id"/>.</param>
/// <param name="SourcePort">시작 노드의 몇 번째 출력 포트에서 나가는지(0부터 시작). N개 출력 포트를 가진 노드를 지원하기 위한 필드입니다.</param>
/// <param name="TargetNodeId">이 연결선이 도착하는 노드의 <see cref="NodeConfig.Id"/>.</param>
/// <param name="TargetPort">도착 노드의 몇 번째 입력 포트로 들어가는지(0부터 시작).</param>
/// <example>
/// <code>
/// // Inject 노드(n1)의 0번 출력 포트 → Function 노드(n2)의 0번 입력 포트
/// var wire = new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0);
/// </code>
/// </example>
public sealed record Wire(string SourceNodeId, int SourcePort, string TargetNodeId, int TargetPort);
