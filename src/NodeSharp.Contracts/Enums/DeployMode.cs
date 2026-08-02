namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : 배포 모드
/// 역활 및 기능 : FlowEngine.DeployAsync가 재시작할 범위(Full/ModifiedFlows/ModifiedNodes/RestartFlows)를 지정하는 배포 모드
///
/// Editor의 [배포] 버튼을 눌렀을 때 <c>FlowEngine.DeployAsync</c>가 "얼마나 넓은 범위를
/// 재시작할지" 지정하는 모드입니다. Node-RED의 배포 드롭다운(Full/Modified Flows/Modified
/// Nodes/Restart Flows)에 정확히 대응합니다. 값 순서 자체에 "더 넓다/좁다"는 의미는 없고,
/// 각 모드는 서로 다른 기준으로 재시작 대상 노드 집합을 고릅니다.
/// 설계 근거: 02번 문서 3번 탭 카드 5.
/// </summary>
/// <remarks>
/// 항상 전체 재시작만 지원하면, TCP 소켓·MQTT 구독처럼 실행 중인 통신 연결이 있는 노드가
/// 전혀 관련 없는 다른 노드의 사소한 설정 변경 때문에 불필요하게 끊깁니다. 이 Enum으로
/// "정말 바뀐 것만" 재시작할 수 있습니다.
/// </remarks>
/// <example>
/// <code>
/// // Editor의 배포 버튼 드롭다운에서 모드를 선택해 DeployAsync를 호출하는 예
///
/// // 기본값 — 설정이 실제로 바뀐 노드만 재시작, 연결이 끊기지 않아 가장 안전
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
    /// <summary>전체 노드를 정지 후 재시작합니다. 최초 배포, 노드 타입 플러그인 교체 직후 등에 사용합니다.</summary>
    Full,

    /// <summary>변경된 <see cref="Models.FlowDefinition"/>(Flow 탭)에 속한 노드만 재시작합니다. 다른 탭의 노드는 건드리지 않습니다.</summary>
    ModifiedFlows,

    /// <summary>이전 설정과 필드 단위로 비교해 속성이 실제로 변경된 노드만 재시작합니다. 4가지 중 가장 안전한 기본 동작입니다.</summary>
    ModifiedNodes,

    /// <summary>설정 변경 없이 전체 노드를 재시작만 합니다. 통신 상태가 이상할 때 강제로 연결을 리셋하고 싶을 때 사용합니다.</summary>
    RestartFlows
}
