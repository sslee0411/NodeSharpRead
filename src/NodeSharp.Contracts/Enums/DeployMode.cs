namespace NodeSharp.Contracts.Enums;

/// <summary>
/// <c>FlowEngine.DeployAsync</c> 호출 시 "얼마나 넓은 범위를 재시작할지"를 지정하는 배포 모드입니다.
/// 값이 클수록(엄밀히는 뒤 항목일수록) 재시작 범위가 넓어지는 것이 아니라, 각 모드는 서로 다른
/// 기준으로 재시작 대상 노드 집합을 선택합니다 — 순서 자체에 의미는 없습니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 3번 탭(실행·배포 흐름) 카드 5 "배포(Deploy) 모드 세분화".
/// Node-RED도 에디터에서 "배포" 버튼을 누를 때 Full/Modified Flows/Modified Nodes/Restart Flows
/// 4가지 모드를 드롭다운으로 선택할 수 있는데, 이 Enum이 그 4가지에 정확히 대응합니다.
/// </para>
/// <para>
/// 목적: 지금까지 <c>DeployAsync</c>가 항상 "전체 노드 정지 후 전체 재시작"만 지원한다면,
/// 실행 중인 통신 연결(예: TCP 소켓, MQTT 구독)이 있는 노드가 <b>전혀 관련 없는 다른 노드의
/// 사소한 설정 변경</b> 때문에 불필요하게 끊기는 문제가 생깁니다. 이 Enum으로 "정말 바뀐 것만"
/// 재시작할 수 있게 합니다.
/// </para>
/// </remarks>
/// <example>
/// Editor의 배포 버튼 드롭다운에서 모드를 선택해 <c>DeployAsync</c>를 호출하는 예:
/// <code>
/// // 기본값(Node-RED 기본값과 동일) — 설정이 실제로 바뀐 노드만 재시작, 연결이 끊기지 않아 가장 안전
/// await flowEngine.DeployAsync(newFlow, DeployMode.ModifiedNodes, cancellationToken);
///
/// // 노드 타입 DLL을 새로 추가/교체한 뒤 처음 배포할 때는 전체 재시작이 필요
/// await flowEngine.DeployAsync(newFlow, DeployMode.Full, cancellationToken);
///
/// // 설정은 그대로인데 PLC 재연결 등 "리셋"만 하고 싶을 때
/// await flowEngine.DeployAsync(currentFlow, DeployMode.RestartFlows, cancellationToken);
/// </code>
/// </example>
public enum DeployMode
{
    /// <summary>
    /// 전체 노드를 정지 후 전체 재시작합니다(가장 단순하고 확실하지만, 관련 없는 노드의 연결도 모두 끊깁니다).
    /// 최초 배포, 노드 타입 플러그인 교체 직후 등에 사용합니다.
    /// </summary>
    Full,

    /// <summary>
    /// 변경된 Flow 탭(<see cref="object">FlowDefinition</see>)에 속한 노드만 재시작합니다.
    /// 다른 Flow 탭의 노드는 전혀 건드리지 않습니다.
    /// </summary>
    ModifiedFlows,

    /// <summary>
    /// 이전 <c>NodeConfig</c>와 필드 단위로 비교해, 속성이 실제로 변경된 노드만 재시작합니다.
    /// Node-RED의 "배포" 기본 동작과 동일하며, 4가지 모드 중 <b>가장 안전</b>합니다(불필요한 재시작 최소).
    /// </summary>
    ModifiedNodes,

    /// <summary>
    /// 설정 변경 없이 전체 노드를 재시작만 합니다. 통신 연결을 강제로 리셋하고 싶을 때(예: PLC
    /// 통신 상태가 이상해 보일 때 "새로고침" 목적)처럼, 내용은 그대로인데 재기동만 원할 때 사용합니다.
    /// </summary>
    RestartFlows
}
