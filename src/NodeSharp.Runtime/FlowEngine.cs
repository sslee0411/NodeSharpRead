using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 플로우 실행 엔진
/// 역활 및 기능 : Editor/Runner가 공유하는 플로우 배포·라우팅·생명주기 관리 실행 엔진
///
/// 플로우 실행 엔진. Editor(미리보기 실행)와 Runner(운영 실행) 양쪽이 공유하는 순수 로직입니다
/// (WPF 비의존, 1번 탭 카드2). 이 클래스는 <c>RT-01~11</c>에 걸쳐 증분으로 완성됩니다 — 지금까지는
/// <see cref="CreateInstance"/>(<c>RT-01a</c>), Full 모드 <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>(<c>RT-01b</c>),
/// <see cref="MissingNode"/> 대체(<c>RT-02a</c>), CreateInstance/OnStartAsync 두 단계 전체 예외 격리
/// (<c>RT-02b</c>), <see cref="DeployMode"/> 4종에 따른 부분 재배포(<c>RT-03</c>), 1:1 Wire 메시지 전달
/// (<see cref="RouteAsync"/>, <c>RT-04a</c>), 출력 하나가 여러 Wire로 나가는 Fan-out 순차/병렬 하이브리드
/// (<c>RT-04b</c>), 순환 구조 hop-count 안전장치(<c>RT-05</c>), 노드별 동시성 제한(<see cref="NodeExecutionGate"/>,
/// <c>RT-06</c>), <see cref="BuildContext"/>가 실제 <see cref="NodeContext"/>(<c>Local</c>/<c>Flow</c>/
/// <c>Global</c>/<c>Env</c> 4개 스코프 + <c>RouteAsync</c>/<c>SetStatus</c>)를 만들도록 교체(<c>RT-09b</c>)
/// 까지 있습니다("뼈대 우선, 확장" 원칙, 03번 Step맵 카드1).
/// 설계 근거: 02번 문서 2번 탭 카드 4·카드 9(정식 기준본)·카드 10(FlowDefinition/NodeConfig 정식 선언),
/// 3번 탭 카드 3(노드 생명주기 시퀀스)·카드 4(메시지 파이프라인 시퀀스)·카드 5(배포 모드 세분화)·
/// 카드 6(배포 예외 격리 — <c>BuildContext</c> 참조부), 5번 탭 카드 1(Fan-out 순차/병렬 하이브리드)·
/// 카드 2(순환 구조 hop-count 안전장치)·카드 3(병렬 처리 — 노드별 동시성 제한).
/// </summary>
/// <remarks>
/// <see cref="_registry"/>가 실제 인스턴스 생성을 담당합니다(<c>NodeSharp.Registry.NodeTypeRegistry</c>,
/// <c>CT-06b</c>+<c>RT-01a</c>) — <see cref="FlowEngine"/> 자체는 타입 조회 방법을 모르고 <see cref="INodeRegistry"/>
/// 계약에만 의존합니다.
/// <para>
/// (★ RT-01b) <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>는 02번 문서 2번 탭 카드4 원본 스니펫처럼 <b>두 단계</b>로 나뉩니다 —
/// 먼저 <see cref="FlowDefinition.Nodes"/> 전체를 <see cref="CreateInstance"/>로 생성해 <see cref="Nodes"/>에
/// 채운 뒤(1단계), 그 다음에야 생성된 노드 전체의 <c>OnStartAsync</c>를 순서대로 호출합니다(2단계) —
/// 완료 기준이 요구하는 "CreateInstance→OnStartAsync 순으로, 순서가 뒤바뀌지 않고 호출"을 이 두 단계
/// 구조로 보장합니다. <see cref="Nodes"/>는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>로
/// 키를 삼습니다 — <c>RT-01a</c>에서 <c>IFlowNode.Id</c>↔<c>NodeConfig.Id</c> 동기화를 의도적으로
/// <c>RG-01</c>로 미뤄뒀기 때문에(<c>Activator.CreateInstance</c>로 만든 노드는 자체 Id를 가질 수 있음),
/// <c>Wire.SourceNodeId</c>/<c>TargetNodeId</c>(2번 탭 카드2)가 참조하는 안정적인 식별자인
/// <see cref="NodeConfig.Id"/>로 관리해야 이후 <c>RT-04a</c>(메시지 라우팅)가 Wire 기반 조회를 그대로
/// 쓸 수 있습니다.
/// </para>
/// <para>
/// (★ RT-01b) <see cref="BuildContext"/>는 02번 문서 3번 탭 카드6·2번 탭 카드8(2602행)에 <c>BuildContext(node)</c>
/// 호출부만 있고 정식 선언이 없던 공백입니다(<c>NodeRef</c>와 동일 유형) — 지금은 <c>NodeContext</c>
/// (Runtime 구체 클래스, <c>RT-09</c>)가 아직 없어 <see cref="INodeContext"/>의 임시 무동작(no-op) 구현인
/// <c>NoOpNodeContext</c>를 반환합니다. <c>RT-09</c>에서 실제 <c>NodeContext</c>로 교체될 때까지의
/// 임시 자리표시자이며, <see cref="MissingNode"/>와 동일한 "타입 시스템을 만족시키는 최소 스텁" 성격입니다
/// (★ RT-09b: 실제로 <see cref="NodeContext"/>로 교체되면서 <c>NoOpNodeContext</c>는 제거됐습니다 —
/// 아래 <see cref="BuildContext"/> 문서 참고).
/// </para>
/// <para>
/// ★ MissingNode 한줄 요약(★ RT-02a): <see cref="MissingNode"/>는 <b>"노드 타입을 찾을 수 없을 때"만</b>
/// 쓰는 자리표시자입니다 — 2단계(기동)에서는 <see cref="MissingNode"/>를 만나면 <c>OnStartAsync</c>
/// 호출 자체를 건너뜁니다(자리표시자는 "기동" 개념이 없음).
/// </para>
/// <para>
/// (★ RT-02b) <see cref="DeployAsync(FlowDefinition, CancellationToken)"/>의 두 단계 모두 노드별로 예외를 격리합니다. 1단계(생성)에서는
/// <c>RT-02a</c>가 좁게 잡던 <see cref="InvalidOperationException"/>(등록되지 않은 타입) 대신 02번 문서
/// 2번 탭 카드4 원본과 동일하게 <b>모든 예외</b>를 잡아 <see cref="MissingNode"/>로 대체합니다(타입은
/// 찾았지만 생성자에서 예외를 던지는 경우 등도 포함). 2단계(기동)에서는 <c>OnStartAsync</c>가 예외를
/// 던지면(예: 잘못된 IP 주소) 그 노드만 <see cref="FailedNodeIds"/>에 기록하고 나머지 노드는 계속
/// 정상 기동합니다 — "설정 오류 하나가 전체 시스템을 멈추면 안 된다"는 원칙(3번 탭 카드6). 두 단계
/// 모두 <c>NodeErrorEvent</c> 발행은 <c>EventBus</c>(<c>RT-07</c>)가 아직 없어 범위 밖입니다.
/// </para>
/// <para>
/// (★ RT-03) <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>는 02번 문서 3번 탭
/// 카드5 <c>DeployMode</c> 4종(<c>Full/ModifiedFlows/ModifiedNodes/RestartFlows</c>)에 따라 재배포 범위를
/// 좁힙니다. 착수 중 발견한 공백과 그 처리:
/// <list type="bullet">
/// <item><b><c>DiffNodeConfigs</c> 정식 선언 없음</b> — 카드5 의사코드는 <c>DiffNodeConfigs(_currentFlow, newFlow)</c>를
/// 호출부만 보여줍니다. <c>NodeConfig.cs</c> remarks(CT 단계 정식 선언 시점에 이미 명시)가 "record 기본
/// <c>==</c>는 <see cref="NodeConfig.Properties"/> 딕셔너리를 참조 비교하므로 RT-03은 필드 단위로 비교해야
/// 한다"고 지시하므로, <see cref="NodeConfigsDiffer"/>로 Id를 제외한 전 필드(Type/Name/FlowId/
/// OutputDispatch/MaxConcurrency/CredentialRefId/Disabled/Properties 키-값)를 비교하도록 구현했습니다.</item>
/// <item><b><c>ChangedFlowIds</c> 정식 선언 없음</b> — 카드5 의사코드는 필드처럼 참조만 하고 계산 방법이
/// 없습니다. 직전 배포(<see cref="_currentFlow"/>)와 이번 <c>newFlow</c>를 노드 단위로 비교해 "추가/변경/
/// 삭제된 노드가 속한 <see cref="NodeConfig.FlowId"/> 집합"으로 정의했습니다 — <c>ModifiedFlows</c>가
/// <c>ModifiedNodes</c>보다 넓은 범위(같은 탭 안의 무변경 노드까지 함께 재시작)라는 카드5 설명과 일치하는
/// 가장 단순한 해석입니다.</item>
/// <item><b>삭제된 노드 처리</b> — 카드5 의사코드는 <c>newFlow</c>에 없어진 기존 노드를 다루지 않습니다.
/// 이번 Step에서는 재시작 대상 범위(<c>Full</c>: 전체, <c>ModifiedNodes</c>: 변경분, <c>ModifiedFlows</c>:
/// 변경된 탭 전체) 안에 있으면서 더 이상 <c>newFlow.Nodes</c>에 없는 기존 노드는 <c>OnCloseAsync</c> 호출 후
/// <see cref="Nodes"/>에서 제거합니다("정지 후 재시작" 원칙의 자연스러운 연장 — 재생성할 새 설정이 없으므로
/// 재생성 없이 제거만 함). <c>RestartFlows</c>는 "설정 변경 없이 재시작"이 전제이므로 diff를 계산하지 않고
/// 카드5 의사코드 그대로 <see cref="Nodes"/> 전체를 재시작 대상으로 삼습니다.</item>
/// <item><b>기존 2-인자 <see cref="DeployAsync(FlowDefinition, CancellationToken)"/> 유지</b> — 카드5는
/// <c>DeployAsync(FlowDefinition, DeployMode, CancellationToken)</c> 시그니처만 보여주지만, <c>RT-01b</c>부터
/// 있던 2-인자 오버로드를 제거하면 기존 <c>RT-01b/02a/02b</c> 테스트가 모두 깨집니다. 2-인자 오버로드는
/// <c>DeployMode.Full</c>로 위임하는 얇은 래퍼로 남겨 하위 호환을 유지합니다.</item>
/// </list>
/// 각 모드가 고르는 재시작 대상 집합은 <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>의
/// <c>switch</c>를 참고하십시오. 재시작 대상 노드는 (1) 기존 인스턴스가 있으면 <c>OnCloseAsync</c> 호출 →
/// (2) <c>newFlow.Nodes</c> 순서대로 <see cref="CreateInstance"/>(예외는 <c>RT-02b</c>와 동일하게 <see cref="MissingNode"/>로 흡수)
/// → (3) 새로 생성된 노드만 순서대로 <c>OnStartAsync</c>(실패는 <c>RT-02b</c>와 동일하게 <see cref="FailedNodeIds"/>에 기록,
/// <see cref="MissingNode"/>는 건너뜀) 순으로 처리됩니다. 재시작 대상이 아닌 기존 노드는 인스턴스를 그대로
/// 유지합니다(연결이 끊기지 않음 — 이 Step의 존재 이유).
/// </para>
/// <para>
/// (★ RT-04a) <see cref="RouteAsync"/>는 02번 문서 2번 탭 카드4 원본 스니펫(<c>RouteAsync(fromNodeId,
/// outputPort, msg, ct)</c> — <c>_wires.Where(...)</c>로 대상을 찾아 <c>OnInputAsync</c> 호출)을 그대로
/// 구현하되, 1:1 Wire 배달만 이 Step 범위입니다(출력 하나가 여러 Wire로 나가는 Fan-out의 순차/병렬
/// 분기는 카드9·<c>RT-04b</c> 몫). 착수 중 발견한 공백과 그 처리:
/// <list type="bullet">
/// <item><b>Wire 저장소가 없음</b> — 카드4 원본은 <c>_wires</c> 필드에 <c>DeployAsync</c>가
/// <c>_wires.AddRange(flow.Wires)</c>로 계속 누적하지만, <c>RT-03</c>이 이미 부분 재배포를 도입해
/// <c>DeployAsync</c>가 여러 번 호출되는 구조가 됐으므로 그대로 누적하면 같은 Wire가 중복 등록됩니다.
/// 별도 <c>_wires</c> 필드를 새로 두는 대신, <c>RT-03</c>이 이미 관리하는 <see cref="_currentFlow"/>
/// (매 배포마다 최신 <see cref="FlowDefinition"/>으로 교체됨)의 <c>Wires</c>를 그대로 조회해 사용합니다 —
/// 별도 동기화 없이 항상 "가장 최근 배포된 Wire 목록"을 반영합니다.</item>
/// <item><b>Step맵 RT-04a 완료 기준 문구 정정</b> — 03번 Step맵 원문은 "A의 <c>OnInputAsync</c> 반환
/// <c>Msg</c>가 B의 <c>OnInputAsync</c> 인자로 전달"이라고 서술하지만, 이미 확정·구현된
/// <see cref="IFlowNode.OnInputAsync"/> 계약(<c>CT-04a</c>, 반환값 없는 <c>Task</c>)은 노드가
/// <c>ctx.RouteAsync(...)</c>를 직접 호출하는 콜백 방식입니다(02번 문서 2번 탭 카드1 <c>PassThroughNode</c>
/// 예제, 5번 탭 <c>FunctionNode</c> 예제 등 전부 이 방식). "반환된 Msg"는 <c>CT-04a</c> 이전 표현이 갱신되지
/// 않고 남은 문구로 판단해, 실제 계약(노드가 <c>ctx.RouteAsync</c>로 명시 호출한 <c>Msg</c>가 Wire를 따라
/// 다음 노드의 <c>OnInputAsync</c> 인자로 전달됨)에 맞춰 03번 Step맵 설명을 정정했습니다(판단 근거가
/// 이미 확정된 인터페이스 계약이라 사용자 확인 없이 직접 반영, RT-01a/RT-02a 등과 동일한 처리 원칙).</item>
/// <item><b>대상 노드별 예외 격리는 범위 밖</b> — 카드4 원본 <c>RouteAsync</c>에는 <c>try/catch</c>가 없고,
/// 이 Step 완료 기준도 정상 경로(1:1 전달)만 요구합니다. 여러 대상 중 하나의 <c>OnInputAsync</c>가 예외를
/// 던지면 나머지 대상에게 전달되지 않고 예외가 그대로 전파됩니다 — <c>DeployAsync</c>의 "노드 하나의
/// 문제가 전체를 막지 않는다" 원칙(<c>RT-02b</c>)을 라우팅에도 적용할지는 향후 Step(<c>RT-06</c> 동시성
/// 제한과 함께 재검토 예상)에서 다룰 별도 판단 사안으로 남겨둡니다.</item>
/// </list>
/// <see cref="BuildContext"/>가 만들던 <c>NoOpNodeContext</c>의 <c>RouteAsync</c>는 이 시점부터
/// <see cref="FlowEngine.RouteAsync"/>로 실제 위임합니다(더 이상 무동작이 아님) — <c>SetStatus</c>만
/// <c>RT-07</c> EventBus 연동 전까지 계속 무동작이라 클래스 이름은 그대로 유지했습니다(★ RT-09b:
/// <c>SetStatus</c>도 실제 발행으로 교체되면서 <c>NoOpNodeContext</c> 자체가 <see cref="NodeContext"/>로
/// 대체·제거됐습니다).
/// </para>
/// <para>
/// (★ RT-04b) <see cref="RouteAsync"/>는 05번 탭(동작모델) 카드1 원본 스니펫대로 <see cref="NodeConfig.OutputDispatch"/>가
/// <c>DispatchMode.Parallel</c>이면 <c>Task.WhenAll</c>로 모든 대상에 동시 전달하고, 기본값
/// <c>Sequential</c>이면 Wire 순서대로 하나씩 <c>await</c>합니다(대상별 <c>OnInputAsync</c>는 여전히
/// <see cref="DispatchOneAsync"/>로 위임, 각 대상은 <c>msg.Clone()</c>을 받아 분기 간 데이터가 격리됨 —
/// <c>RT-04a</c>와 동일한 격리 원칙). 착수 중 발견한 공백과 그 처리:
/// <list type="bullet">
/// <item><b>refcell 오기</b> — 03번 Step맵 <c>RT-04b</c> 행의 설계 근거 칸이 "2번 탭"으로 돼 있었지만,
/// <c>DispatchMode</c> 기반 <c>RouteAsync</c> 확장 코드의 실제 위치는 5번 탭(동작모델) 카드1입니다(grep으로
/// 원문 위치 확인). 이미 확정된 카드 번호를 가리키는 참조 오류라 사용자 확인 없이 정정(<c>RT-02b</c>
/// refcell 정정과 동일한 처리 원칙).</item>
/// <item><b><c>fromNode.Config.OutputDispatch</c> — <see cref="IFlowNode"/>에 없는 멤버</b> — 카드1 원본
/// 스니펫은 <c>_nodes[fromNodeId].Config.OutputDispatch</c>로 발신 노드의 <c>DispatchMode</c>를 읽지만,
/// <see cref="IFlowNode"/>(<c>CT-04a</c>로 이미 확정)에는 <c>Config</c> 프로퍼티가 없습니다 — 노드
/// 인스턴스가 자신을 만든 <see cref="NodeConfig"/>를 들고 있지 않기 때문입니다(<c>RT-01a</c>에서
/// <c>CreateInstance</c>가 <c>Name</c>만 동기화하고 나머지는 옮기지 않음). <see cref="IFlowNode"/>에
/// 멤버를 추가하는 대신, <c>RT-03</c>부터 이미 있는 <see cref="_currentFlow"/>.Nodes에서
/// <paramref name="fromNodeId"/>와 일치하는 <see cref="NodeConfig"/>를 찾아 <c>OutputDispatch</c>를
/// 읽습니다(찾지 못하면 <c>DispatchMode.Sequential</c> 기본값 — 발신 노드 설정을 알 수 없는 경우
/// Node-RED 기본 동작과 동일하게 안전한 쪽으로 처리).</item>
/// </list>
/// </para>
/// <para>
/// (★ RT-05) <see cref="RouteAsync"/> 맨 앞에 05번 탭 카드2 원본과 같은 hop-count(거쳐온 횟수) 가드를
/// 추가했습니다. <see cref="Msg.HopCount"/>가 <see cref="MaxHopCount"/> 이상이면 라우팅을 멈춥니다.
/// A→B→A처럼 서로를 계속 부르는 순환 Wire 구조에서는, 한 노드가 <c>OnInputAsync</c> 안에서
/// <c>ctx.RouteAsync</c>를 또 호출하고 그 안에서 다시 호출하는 식으로 메서드 호출이 끝없이 이어질 수
/// 있습니다. 이 가드가 그 무한 호출을 막아줍니다. 착수 중 발견한 공백과 그 처리:
/// <list type="bullet">
/// <item><b>refcell 오기</b> — 03번 Step맵 <c>RT-05</c> 행의 설계 근거 칸도 <c>RT-04b</c>와 같은 유형으로
/// "2번 탭"이었지만, hop-count 가드 원본 코드의 실제 위치는 5번 탭 카드2입니다 — 동일한 원칙(<c>RT-02b</c>·
/// <c>RT-04b</c> refcell 정정)으로 "5번 탭 카드 2"로 정정.</item>
/// <item><b><c>MaxHopCount</c>를 상수가 아니라 프로퍼티로</b> — 카드2 원본은 <c>private const int MaxHopCount = 1000</c>이지만
/// 주석에 "설정 파일로 조정 가능"이라고 명시돼 있습니다. 설정 파일 인프라가 아직 없어(향후 Step), 지금은
/// 공개 <c>{ get; set; }</c> 프로퍼티(기본값 1000)로 두어 나중에 설정 파일 로딩 코드가 이 값을 바꿀 수
/// 있게 하고, 테스트도 작은 값으로 빠르게 검증할 수 있게 했습니다.</item>
/// <item><b><c>FlowLoopGuardTrippedEvent</c> → <c>LoopGuardTrips</c></b> — 카드2 원본은 <c>EventBus.Publish</c>로
/// 경고 이벤트를 발행하지만 <c>EventBus</c>(<c>RT-07</c>)가 아직 없습니다. <see cref="FailedNodeIds"/>
/// (<c>RT-02b</c>)와 동일한 선례로, 실제 이벤트 대신 관찰 가능한 <see cref="LoopGuardTrips"/> 프로퍼티에
/// (발신 노드 Id, 중단된 <see cref="Msg.Id"/>) 기록만 남깁니다 — <c>RT-07</c> 이후 실제
/// <c>FlowLoopGuardTrippedEvent</c> 발행으로 교체 예정.</item>
/// <item><b>배포 시 정적 순환 감지(<c>DetectCycles</c>)는 범위 밖</b> — 카드2 원본은 배포 시점 DFS 기반
/// 순환 그래프 탐지(Editor 경고 표시용)도 함께 보여주지만, 03번 Step맵을 확인한 결과 이 부분은 이미
/// <c>OP-04</c>(FlowLinter 통합, "순환 참조" 검사 포함, Phase 10)가 정식으로 담당하도록 별도 Step이
/// 배정돼 있어 중복 구현하지 않았습니다 — <c>RT-05</c>는 런타임 hop-count 가드만 담당.</item>
/// </list>
/// </para>
/// <para>
/// (★ RT-06) <see cref="DispatchOneAsync"/>가 대상 노드의 <c>OnInputAsync</c>를 호출하기 전에
/// <see cref="NodeExecutionGate"/>로 동시 실행 개수를 제한합니다(05번 탭 카드3 원본). 착수 중 발견한
/// 공백과 그 처리:
/// <list type="bullet">
/// <item><b>refcell 오기</b> — 03번 Step맵 <c>RT-06</c> 행도 <c>RT-04b</c>/<c>RT-05</c>와 같은 유형으로
/// "2번 탭"이었지만, 실제 코드 위치는 5번 탭 카드3 — 동일한 원칙으로 "5번 탭 카드 3"으로 정정.</item>
/// <item><b>게이트 키를 <c>IFlowNode.Id</c>가 아니라 <c>NodeConfig.Id</c>로</b> — 카드3 원본은
/// <c>_gates[node.Id]</c>를 쓰지만, <c>IFlowNode.Id</c>는 <c>RG-01</c> 전까지 신뢰할 수 없는 값입니다
/// (<c>RT-01b</c>부터 이어진 원칙). <see cref="DispatchOneAsync"/>는 이미 <c>wire.TargetNodeId</c>(안정적인
/// <see cref="NodeConfig.Id"/>)를 알고 있으므로 이 값을 그대로 게이트 키로 사용합니다
/// (<see cref="NodeExecutionGate"/> 자체 문서 참고).</item>
/// <item><b>동시성 상한은 <c>NodeConfig.MaxConcurrency</c> 우선</b> — 카드3 원본은
/// <c>node.MaxConcurrency</c>(<see cref="IFlowNode"/> 기본 구현 멤버, 코드 레벨 타입 기본값)만 참조하지만,
/// <see cref="NodeConfig.MaxConcurrency"/>(<c>CT-02b</c>에 이미 있던 필드 — "5번 탭 <c>IFlowNode.MaxConcurrency</c>
/// 기본값과 동일 의미"라고 스스로 명시)가 사용자가 Editor에서 실제로 지정하는 값이라 우선순위가 더
/// 높아야 합니다(<c>RT-04b</c>의 <c>OutputDispatch</c> 우선순위 결정과 동일한 판단). <see cref="_currentFlow"/>.Nodes에서
/// 대상 <see cref="NodeConfig"/>를 찾아 그 값을 쓰고, 배포 정보를 못 찾을 때만 <see cref="IFlowNode.MaxConcurrency"/>
/// 기본값으로 대체합니다.</item>
/// <item><b>재배포 시 게이트 갱신</b> — <c>MaxConcurrency</c> 설정이 바뀐 채로 재배포되면 기존 게이트가
/// 낡은 상한을 계속 쓰게 되는 공백이 있어, <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>가
/// 노드를 닫을 때(재시작 대상·삭제 대상 모두) <see cref="NodeExecutionGate.RemoveGate"/>도 함께 호출하도록
/// 추가했습니다 — 다음 배포에서 같은 Id로 노드가 다시 만들어지면 새 게이트가 최신 설정으로 생성됩니다.</item>
/// </list>
/// </para>
/// <para>
/// (★ RT-09b) <see cref="BuildContext"/>가 <c>NoOpNodeContext</c> 대신 실제 <see cref="NodeContext"/>
/// (02번 문서 2번 탭 카드9 "정식 통합판" 중 <c>Local</c>/<c>Flow</c>/<c>Global</c>/<c>Env</c> 4개 스코프 +
/// <c>RouteAsync</c>/<c>SetStatus</c>만 우선 구현, <c>Shared</c>/<c>Scheduler</c>/<c>Structure</c>는 아직
/// 없음 — 사용자 확인 완료, 향후 같은 클래스에 멤버만 추가 예정)를 만들도록 바뀌었습니다. 착수 중 발견한
/// 공백과 그 처리:
/// <list type="bullet">
/// <item><b><c>BuildContext</c>가 <c>flowId</c>를 알아야 함</b> — <see cref="NodeContext"/>의
/// <see cref="NodeContext.Flow"/> 스코프는 <c>scopeId</c>로 <c>flowId</c>가 필요하지만, 기존
/// <c>BuildContext(IFlowNode node)</c> 시그니처는 <c>flowId</c>도 <c>NodeConfig.Id</c>도 받지 못했습니다
/// (호출부가 <see cref="IFlowNode"/> 인스턴스만 넘김). 세 호출부(<c>DeployAsync</c> 종료 루프의 <c>id</c>,
/// 기동 루프의 <c>cfgId</c>, <see cref="DispatchOneAsync"/>의 <c>wire.TargetNodeId</c>)가 이미 각자
/// 안정적인 <see cref="NodeConfig.Id"/>를 알고 있었으므로, <c>BuildContext</c>에
/// <c>nodeConfigId</c> 매개변수를 추가해 그 값을 그대로 전달받고, <see cref="_currentFlow"/>.Nodes에서
/// 그 Id로 <c>NodeConfig</c>를 찾아 <c>FlowId</c>를 읽도록 했습니다(찾지 못하면 빈 문자열 — <c>RT-04b</c>의
/// <c>OutputDispatch</c> 조회 실패 시 기본값 대체와 동일한 원칙).</item>
/// <item><b><c>ContextStore</c>/<c>EventBus</c>를 어디서 만들 것인가</b> — 카드9 원본은 <c>FlowEngine</c>이
/// 이미 <c>ContextStore</c>/<c>EventBus</c> 프로퍼티를 갖고 있다고 전제하지만 이 Step 이전에는 없었습니다.
/// 기존 1-인자 생성자(<c>RT-01a</c>부터의 모든 테스트가 사용)를 깨지 않기 위해, 새 생성자 매개변수 2개를
/// 선택적(<c>= null</c>)으로 추가하고 생략 시 각각 새 <see cref="InMemoryContextStore"/>/
/// <see cref="EventBusAdapter"/>를 만들어 씁니다 — 테스트에서는 명시적으로 주입해 격리할 수 있습니다.</item>
/// <item><b><c>NoOpNodeContext</c> 제거</b> — <c>RT-01b</c>부터 있던 사설 중첩 클래스는 더 이상 참조하는
/// 곳이 없어 그대로 제거했습니다(<see cref="NodeContext"/>가 <c>RouteAsync</c>/<c>SetStatus</c> 둘 다
/// 동등하게 대체). <c>SetStatus</c>는 이제 무동작이 아니라 <see cref="IEventBus.Publish{TEvent}"/>로
/// <c>NodeStatusEvent</c>를 실제 발행합니다(<c>RT-07</c> EventBus가 준비된 뒤 처음으로 실사용).</item>
/// <item><b><c>Shared</c>/<c>Scheduler</c>/<c>Structure</c>는 범위 밖</b> — 카드9 원본 <c>NodeContext</c>는
/// 이 3개 멤버도 갖지만, 각각 <c>SharedResourceManager</c>(<c>RT-10</c>, 아직 없음)·<c>IScheduler</c>
/// (<c>RT-08</c>로 어댑터는 있지만 노드별 인스턴스 배선은 별도 Step)·<c>IStructureService</c>
/// (<c>CT-04b</c>로 인터페이스는 있지만 실 구현 연동은 별도 Step)가 필요해 이 Step 범위 밖으로 남겨두고,
/// 준비되는 대로 같은 <see cref="NodeContext"/> 클래스에 멤버만 추가할 예정입니다(새 클래스를 만들지
/// 않기로 사용자와 확인 완료).</item>
/// </list>
/// </para>
/// <para>
/// (LK-02a) <see cref="DispatchOneAsync"/>가 대상 노드를 찾은 직후 <see cref="FlowActivityEvent"/>를
/// 발행하도록 추가됐습니다 — <c>CT-05a</c>에서 이미 정의된 이벤트지만 지금까지는 아무도 발행하지
/// 않았습니다(그 Step 완료 기준도 "정의됐는지"만 요구했고, 실제 발행은 <c>LK-02</c>가 생기면 하기로
/// 명시적으로 미뤄져 있었습니다). Runner의 <c>StatusBroadcaster</c>가 이 이벤트를 구독해 SignalR로
/// Editor에 중계하면 캔버스 와이어가 하이라이트됩니다(Editor UI 반영은 <c>LK-02b</c>). 발행 시점은
/// "<see cref="_nodes"/>에 대상 인스턴스가 실제로 있어 <c>OnInputAsync</c>가 곧 호출될 예정"인 순간
/// (<c>TryGetValue</c> 성공 직후, 동시성 게이트를 기다리기 전)으로 잡아, 배포 자체에 없는 대상(예:
/// 테스트에서 존재하지 않는 <c>NodeConfig.Id</c>로 직접 <c>RouteAsync</c>를 호출한 경우)까지 "메시지가
/// 흘렀다"고 잘못 표시하지 않습니다. 다만 <see cref="MissingNode"/>(등록되지 않은 노드 타입의 대체
/// 자리표시자, <c>RT-02a</c>)는 <see cref="_nodes"/>에 실제로 들어 있어 이 조건을 통과합니다 — Wire
/// 자체는 배포에 존재하고 메시지가 그 와이어를 타고 "시도"된 것은 사실이므로(대상 노드가 무동작인
/// 것과 별개), 캔버스에서 와이어가 하이라이트되는 것은 의도된 동작입니다(어떤 노드가 왜 반응하지
/// 않는지는 사용자가 노드 카드 상태를 보고 판단). <c>Publish</c>(동기)를 쓰는 이유는
/// <see cref="NodeStatusEvent"/>/<see cref="DebugMessageEvent"/>가 이미 쓰고 있는 것과 동일한
/// <see cref="IEventBus"/> 계약이 <c>PublishAsync</c>를 제공하지 않기 때문입니다(구체 클래스
/// <c>NodeSharp.Util.Messaging.EventBus</c>에는 있지만, <see cref="IEventBus"/> 계약에는 없음 —
/// Contracts→Runtime 순환 참조 방지 원칙상 계약을 넓히려면 <c>NR-04</c>/<c>NR-11</c> 때처럼 기존
/// 구현체 전부를 함께 고쳐야 해 이번 Step 범위를 벗어남, 저위험 판단으로 동기 <c>Publish</c> 유지).</para>
/// </remarks>
/// <example>
/// <code>
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
/// registry.TryRegister(new PluginManifest("inject", "1.0.0", "1.0.0"), typeof(InjectNode));
/// var engine = new FlowEngine(registry);
///
/// // RT-01a — 단일 인스턴스 생성
/// var cfg = new NodeConfig("n1", "inject", "타이머", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = engine.CreateInstance(cfg);
///
/// // RT-01b/RT-02a/RT-02b — Full 모드 배포. 등록되지 않은 타입이 섞여 있어도, 기동 중 예외가 나도
/// // 예외 없이 완료된다.
/// var badCfg = new NodeConfig("n2", "no-such-type", "삭제된 플러그인", "f1", new Dictionary&lt;string, object?&gt;());
/// var flow = new FlowDefinition("f1", "테스트", Nodes: new[] { cfg, badCfg }, Wires: Array.Empty&lt;Wire&gt;());
/// await engine.DeployAsync(flow, CancellationToken.None);
/// IFlowNode deployed = engine.Nodes["n1"];         // typeof(InjectNode) 인스턴스
/// IFlowNode missing = engine.Nodes["n2"];           // MissingNode 인스턴스 — 배포는 계속 성공
/// IReadOnlyList&lt;string&gt; failed = engine.FailedNodeIds;   // OnStartAsync 실패 노드 Id 목록
///
/// // RT-03 — n1의 Name만 바꿔 ModifiedNodes로 재배포하면 n1만 재시작되고 n2는 손대지 않는다.
/// var changedCfg = cfg with { Name = "타이머(변경됨)" };
/// var flow2 = flow with { Nodes = new[] { changedCfg, badCfg } };
/// await engine.DeployAsync(flow2, DeployMode.ModifiedNodes, CancellationToken.None);
///
/// // RT-04a — n1(inject) → n3(debug) 1:1 Wire 배포 후, n1이 ctx.RouteAsync로 보낸 Msg가
/// // n3.OnInputAsync 인자로 그대로(Clone되어) 전달된다.
/// var wired = new FlowDefinition("f2", "라우팅 테스트",
///     Nodes: new[] { cfg, new NodeConfig("n3", "debug", "디버그", "f1", new Dictionary&lt;string, object?&gt;()) },
///     Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n3", TargetPort: 0) });
/// await engine.DeployAsync(wired, DeployMode.Full, CancellationToken.None);
/// await engine.RouteAsync("n1", 0, new Msg { Payload = 42 }, CancellationToken.None);   // n3가 42를 받음
///
/// // RT-04b — n1의 0번 출력이 n4/n5 두 곳으로 Fan-out. OutputDispatch가 기본값(Sequential)이면
/// // n4→n5 순서로 하나씩, Parallel이면 Task.WhenAll로 동시에 전달된다 — 어느 쪽이든 각자 다른
/// // Msg 인스턴스를 받아 한쪽에서 Payload를 바꿔도 다른 쪽에 영향이 없다.
/// var fanOutCfg = cfg with { OutputDispatch = DispatchMode.Parallel };   // n1을 병렬 분기로 전환
/// var fanOutFlow = new FlowDefinition("f3", "Fan-out 테스트",
///     Nodes: new[]
///     {
///         fanOutCfg,
///         new NodeConfig("n4", "debug", "알림1", "f1", new Dictionary&lt;string, object?&gt;()),
///         new NodeConfig("n5", "debug", "알림2", "f1", new Dictionary&lt;string, object?&gt;()),
///     },
///     Wires: new[]
///     {
///         new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n4", TargetPort: 0),
///         new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n5", TargetPort: 0),
///     });
/// await engine.DeployAsync(fanOutFlow, DeployMode.Full, CancellationToken.None);
/// await engine.RouteAsync("n1", 0, new Msg { Payload = "알람" }, CancellationToken.None);   // n4·n5 동시 수신
///
/// // RT-05 — 이미 999홉을 돈 Msg를 RouteAsync에 넣으면(기본 MaxHopCount=1000) 1번만 더 라우팅되고
/// // 그 다음 홉에서 자동 차단된다. 테스트에서는 MaxHopCount를 작게 낮춰 빠르게 검증한다.
/// engine.MaxHopCount = 3;
/// var almostLooped = new Msg { Payload = "루프" };
/// almostLooped.HopCount = 3;   // 이미 임계값에 도달한 상태를 시뮬레이션
/// await engine.RouteAsync("n1", 0, almostLooped, CancellationToken.None);   // 대상에게 전달되지 않음
/// var trip = engine.LoopGuardTrips[^1];   // ("n1", almostLooped.Id) — 어떤 노드에서 언제 중단됐는지 확인
///
/// // RT-06 — n6의 MaxConcurrency를 2로 설정하고 동시에 3개를 라우팅하면, 3번째 호출은 앞선 두 건 중
/// // 하나가 끝날 때까지(OnInputAsync 완료 + gate.Release) 대기한 뒤에야 처리된다.
/// var gatedCfg = new NodeConfig("n6", "inject", "제한된 노드", "f1", new Dictionary&lt;string, object?&gt;(), MaxConcurrency: 2);
/// var gatedFlow = new FlowDefinition("f4", "동시성 테스트", Nodes: new[] { gatedCfg }, Wires: Array.Empty&lt;Wire&gt;());
/// await engine.DeployAsync(gatedFlow, DeployMode.Full, CancellationToken.None);
/// var t1 = engine.RouteAsync("src", 0, new Msg(), CancellationToken.None);   // 즉시 통과(1/2)
/// var t2 = engine.RouteAsync("src", 0, new Msg(), CancellationToken.None);   // 즉시 통과(2/2)
/// var t3 = engine.RouteAsync("src", 0, new Msg(), CancellationToken.None);   // t1/t2 중 하나가 끝날 때까지 대기
/// </code>
/// </example>
public sealed class FlowEngine
{
    private readonly INodeRegistry _registry;
    private readonly Dictionary<string, IFlowNode> _nodes = new();
    private readonly List<string> _failedNodes = new();
    private readonly List<(string NodeId, string MsgId)> _loopGuardTrips = new();
    private readonly NodeExecutionGate _gate = new();
    private readonly IContextStore _contextStore;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// (★ RT-03) 직전 <c>DeployAsync</c> 호출에 사용된 <see cref="FlowDefinition"/>입니다. 부분 재배포 모드
    /// (<see cref="DeployMode.ModifiedNodes"/>/<see cref="DeployMode.ModifiedFlows"/>)가 "무엇이 바뀌었는지"
    /// 판단할 비교 기준(baseline)으로 사용합니다. 최초 배포 전에는 <c>null</c>이며, 이 경우 모든 노드를
    /// "추가됨"으로 취급합니다(= Full과 동일하게 전체 생성).
    /// </summary>
    private FlowDefinition? _currentFlow;

    /// <summary>
    /// 노드 타입 조회·인스턴스 생성을 위임할 레지스트리를 받아 엔진을 생성합니다.
    /// (★ RT-09b) <paramref name="contextStore"/>/<paramref name="eventBus"/>는 <see cref="BuildContext"/>가
    /// 만드는 <c>NodeContext</c>가 공유할 재료입니다 — 생략하면 각각 새 <see cref="InMemoryContextStore"/>/
    /// <see cref="EventBusAdapter"/>(<c>EventBus.Instance</c> 감쌈)를 만들어 씁니다. 기존 1-인자 호출부
    /// (<c>RT-01a</c>부터의 모든 테스트)는 그대로 동작합니다(선택적 매개변수라 하위 호환 유지).
    /// </summary>
    public FlowEngine(INodeRegistry registry, IContextStore? contextStore = null, IEventBus? eventBus = null)
    {
        _registry = registry;
        _contextStore = contextStore ?? new InMemoryContextStore();
        _eventBus = eventBus ?? new EventBusAdapter();
    }

    /// <summary>
    /// (★ RT-09b) <see cref="BuildContext"/>가 만드는 모든 <c>NodeContext</c>가 공유하는
    /// <see cref="IContextStore"/>입니다(02번 문서 2번 탭 카드9 <c>FlowEngine.ContextStore</c>). 외부에서
    /// 배포 전 값을 미리 넣거나 테스트에서 직접 조회할 때 사용합니다.
    /// </summary>
    public IContextStore ContextStore => _contextStore;

    /// <summary>
    /// (★ RT-09b) <see cref="BuildContext"/>가 만드는 모든 <c>NodeContext</c>의 <c>SetStatus</c>가
    /// 발행 대상으로 쓰는 <see cref="IEventBus"/>입니다(02번 문서 2번 탭 카드9 <c>FlowEngine.EventBus</c>).
    /// 노드 상태 변경(<c>NodeStatusEvent</c>)을 외부에서 구독하려면 이 프로퍼티로 구독하십시오.
    /// </summary>
    public IEventBus EventBus => _eventBus;

    /// <summary>
    /// <paramref name="cfg"/>.Type에 등록된 노드 타입의 인스턴스를 생성합니다. 실제 조회·생성 로직은
    /// <see cref="INodeRegistry.CreateInstance"/>(구현체: <c>NodeTypeRegistry</c>)에 위임합니다.
    /// <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>는 이 메서드가 던지는 예외를 잡아 <see cref="MissingNode"/>로 대체하지만,
    /// 이 메서드를 직접 호출하면(<c>RT-01a</c> 당시와 동일하게) 예외가 그대로 전파됩니다.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type에 해당하는 등록된 노드 타입이 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg) => _registry.CreateInstance(cfg);

    /// <summary>배포된 노드 목록입니다. Key는 <c>IFlowNode.Id</c>가 아니라 <see cref="NodeConfig.Id"/>입니다(위 remarks 참고).</summary>
    public IReadOnlyDictionary<string, IFlowNode> Nodes => _nodes;

    /// <summary>
    /// (★ RT-02b) <c>OnStartAsync</c> 단계에서 예외가 발생해 기동에 실패한 노드의 <see cref="NodeConfig.Id"/>
    /// 목록입니다. 7번 탭 헬스체크 엔드포인트가 참조할 예정(카드4 <c>FailedNodeIds</c> 원본과 동일 목적).
    /// (★ RT-03) 재배포마다 이번 배포에서 실제로 기동을 시도한 노드 기준으로 다시 계산됩니다 — 이전
    /// 배포에서 실패했던 노드가 이번 배포 대상에서 빠지면(다른 모드로 재시작 범위 밖) 더 이상 이 목록에
    /// 남지 않습니다(항상 "가장 최근 배포 결과"를 반영).
    /// </summary>
    public IReadOnlyList<string> FailedNodeIds => _failedNodes;

    /// <summary>
    /// (★ RT-05) 순환 구조(A→B→A)에서 <see cref="Msg.HopCount"/>가 이 값 이상이 되면 <see cref="RouteAsync"/>가
    /// 라우팅을 중단합니다(02번 문서 5번 탭 카드2 <c>MaxHopCount</c>). 원본 상수(<c>1000</c>)를 "설정 파일로
    /// 조정 가능"하다고 명시했지만 설정 시스템이 아직 없어, 대신 언제든 바꿀 수 있는 공개 프로퍼티로
    /// 노출합니다(테스트에서도 작은 값으로 빠르게 검증할 수 있음).
    /// </summary>
    public int MaxHopCount { get; set; } = 1000;

    /// <summary>
    /// (★ RT-05) <see cref="MaxHopCount"/> 초과로 <see cref="RouteAsync"/>가 라우팅을 중단시킨 이력입니다.
    /// 02번 문서 5번 탭 카드2 원본은 <c>FlowLoopGuardTrippedEvent</c>를 <c>EventBus</c>로 발행하지만,
    /// <c>EventBus</c>(<c>RT-07</c>)가 아직 없어 <see cref="FailedNodeIds"/>(<c>RT-02b</c>)와 동일한 방식의
    /// 임시 대체(관찰 가능한 프로퍼티)로 구현했습니다 — <c>RT-07</c> 이후 실제 이벤트 발행으로 교체될
    /// 예정이며, 그 전까지는 이 목록으로 "언제 어떤 노드에서 순환이 감지됐는지" 확인할 수 있습니다.
    /// </summary>
    public IReadOnlyList<(string NodeId, string MsgId)> LoopGuardTrips => _loopGuardTrips;

    /// <summary>
    /// (★ RT-03) 하위 호환용 2-인자 오버로드입니다. <c>RT-01b</c>부터 있던 기존 시그니처를 유지하기 위해
    /// <see cref="DeployMode.Full"/>로 <see cref="DeployAsync(FlowDefinition, DeployMode, CancellationToken)"/>에
    /// 위임합니다 — 동작은 이전과 동일합니다(전체 정지 후 전체 재시작).
    /// </summary>
    public Task DeployAsync(FlowDefinition flow, CancellationToken ct) =>
        DeployAsync(flow, DeployMode.Full, ct);

    /// <summary>
    /// (★ RT-03) <paramref name="mode"/>에 따라 재배포 범위를 좁혀 적용합니다(02번 문서 3번 탭 카드5).
    /// 재시작 대상으로 뽑힌 노드만 (기존 인스턴스가 있으면) <c>OnCloseAsync</c> → <see cref="CreateInstance"/>
    /// (예외는 <see cref="MissingNode"/>로 흡수, <c>RT-02b</c>) → <c>OnStartAsync</c>(실패는 <see cref="FailedNodeIds"/>에
    /// 기록, <c>RT-02b</c>) 순으로 처리되고, 대상 밖 노드는 기존 인스턴스를 그대로 유지합니다(연결 유지).
    /// <paramref name="flow"/>.Nodes에서 사라진 기존 노드는(재시작 대상 범위 안이면) <c>OnCloseAsync</c> 후
    /// <see cref="Nodes"/>에서 제거됩니다. 모드별 재시작 대상 판단 기준은 클래스 remarks(★ RT-03)를 참고하십시오.
    /// </summary>
    public async Task DeployAsync(FlowDefinition flow, DeployMode mode, CancellationToken ct)
    {
        var oldById = _currentFlow?.Nodes.ToDictionary(n => n.Id) ?? new Dictionary<string, NodeConfig>();
        var newById = flow.Nodes.ToDictionary(n => n.Id);

        // ★ RT-03: 변경분(추가/필드변경)과 삭제분은 모드에 상관없이 항상 같은 기준으로 먼저 계산한다 —
        //   ModifiedNodes는 이 변경분 자체를, ModifiedFlows는 이 변경분이 속한 FlowId 전체를 재시작 대상으로 삼는다.
        var changedIds = new HashSet<string>(
            newById.Where(kv => !oldById.TryGetValue(kv.Key, out var old) || NodeConfigsDiffer(old, kv.Value))
                   .Select(kv => kv.Key));
        var removedIds = new HashSet<string>(oldById.Keys.Except(newById.Keys));

        HashSet<string> restartIds;   // newById 기준 — 새로 생성/재시작할 노드 Id
        HashSet<string> closeOnlyIds; // oldById 기준이지만 newById에는 없음 — 재생성 없이 닫고 제거만 할 노드 Id

        switch (mode)
        {
            case DeployMode.ModifiedNodes:
                // 이전 설정과 필드 단위로 비교해 실제로 변경된 노드만(가장 안전, 카드5)
                restartIds = changedIds;
                closeOnlyIds = removedIds;
                break;

            case DeployMode.ModifiedFlows:
                // 변경/삭제된 노드가 속한 FlowId(탭) 전체를 재시작 대상으로 넓힌다(★ RT-03 ChangedFlowIds 정의)
                var changedFlowIds = new HashSet<string>(
                    changedIds.Select(id => newById[id].FlowId)
                        .Concat(removedIds.Select(id => oldById[id].FlowId)));
                restartIds = new HashSet<string>(newById.Where(kv => changedFlowIds.Contains(kv.Value.FlowId)).Select(kv => kv.Key));
                closeOnlyIds = new HashSet<string>(removedIds.Where(id => changedFlowIds.Contains(oldById[id].FlowId)));
                break;

            case DeployMode.Full:
            case DeployMode.RestartFlows:
            default:
                // 카드5 의사코드 그대로 — 두 모드 모두 전체 재시작(Full은 설정도 새로 반영, RestartFlows는
                // "설정 변경 없이"가 전제이므로 newFlow==currentFlow 상황에서 호출되는 것을 기대함).
                restartIds = new HashSet<string>(newById.Keys);
                closeOnlyIds = removedIds;
                break;
        }

        // 1단계: 재시작 대상(기존 인스턴스가 있는 것만) + 삭제 대상을 먼저 닫는다 — 카드5 의사코드의
        //   "foreach (var node in toRestart) await node.OnCloseAsync(...)"에 삭제분 처리를 더한 것.
        foreach (var id in restartIds.Concat(closeOnlyIds))
        {
            if (_nodes.TryGetValue(id, out var existing))
            {
                try
                {
                    await existing.OnCloseAsync(BuildContext(id, existing));
                }
                catch (Exception)
                {
                    // ★ RT-03: 종료 단계 예외도 RT-02b와 동일한 원칙으로 흡수 — 노드 하나의 종료 실패가
                    //   재배포 전체를 막아서는 안 된다(연결이 이미 끊겨 있는 등 종료 자체가 실패할 수 있음).
                }
            }

            // ★ RT-06: MaxConcurrency 설정이 바뀐 채로 재배포될 수 있으므로, 닫히는 노드의 게이트도 함께
            //   제거한다 — 다음 배포에서 같은 Id로 노드가 다시 만들어지면 최신 설정으로 새 게이트가 생성된다.
            _gate.RemoveGate(id);
        }

        foreach (var id in closeOnlyIds)
        {
            _nodes.Remove(id);
        }

        _failedNodes.Clear();   // ★ RT-03: 항상 "이번 배포"의 기동 실패만 반영(이전 배포의 실패 기록이 누적되지 않음)

        // 2단계: 재시작 대상만 newFlow.Nodes 순서대로 재생성(RT-02b와 동일한 예외 격리)
        var created = new List<(string CfgId, IFlowNode Node)>();
        foreach (var cfg in flow.Nodes)
        {
            if (!restartIds.Contains(cfg.Id)) continue;

            IFlowNode node;
            try
            {
                node = CreateInstance(cfg);
            }
            catch (Exception)
            {
                node = new MissingNode(cfg.Id, cfg.Type);
            }

            _nodes[cfg.Id] = node;
            created.Add((cfg.Id, node));
        }

        // 3단계: 새로 생성된 노드만 순서대로 기동(RT-02b와 동일한 예외 격리) — 재시작 대상이 아니었던
        //   기존 노드는 이 루프에 아예 들어오지 않으므로 OnStartAsync가 다시 호출되지 않는다(연결 유지).
        foreach (var (cfgId, node) in created)
        {
            if (node is MissingNode) continue;   // ★ RT-02a: 자리표시자는 OnStartAsync 자체가 없음

            try
            {
                await node.OnStartAsync(BuildContext(cfgId, node), ct);
            }
            catch (Exception)
            {
                _failedNodes.Add(cfgId);
            }
        }

        _currentFlow = flow;   // ★ RT-03: 다음 배포의 diff 기준선을 이번 배포로 갱신
    }

    /// <summary>
    /// <paramref name="fromNodeId"/>의 <paramref name="outputPort"/>번 출력에 연결된 모든 Wire(직전 배포
    /// <see cref="_currentFlow"/>.Wires 기준)를 따라 <paramref name="msg"/>를 대상 노드의 <c>OnInputAsync</c>로
    /// 전달합니다(02번 문서 2번 탭 카드4 원본 + 5번 탭 카드1 Fan-out 확장·카드2 hop-count 가드,
    /// <c>RT-04a/RT-04b/RT-05</c>). (★ RT-05) 시작하자마자 <paramref name="msg"/>.<see cref="Msg.HopCount"/>
    /// (지금까지 거쳐온 횟수)가 <see cref="MaxHopCount"/> 이상이면 <see cref="LoopGuardTrips"/>에 기록만
    /// 남기고 라우팅을 멈춥니다. A→B→A 같은 순환 구조에서는 노드가 <c>OnInputAsync</c> 안에서
    /// <c>ctx.RouteAsync</c>를 또 부르고, 그 안에서 이 메서드가 다시 호출되는 식으로 매 홉마다 호출이
    /// 반복됩니다. 이 가드가 없으면 이 호출이 끝없이 이어져 메서드 호출 스택이 계속 쌓이게 됩니다.
    /// 가드를 통과하면 <c>HopCount</c>를 1 늘린 뒤(모든 분기가 늘어난 값을 함께 쓰도록 복제 전에 미리
    /// 수행) 발신 노드의 <see cref="NodeConfig.OutputDispatch"/>가 <c>Parallel</c>이면 모든 대상에게
    /// <c>Task.WhenAll</c>로 한꺼번에 전달합니다(도착 순서는 보장되지 않음). 기본값인 <c>Sequential</c>이면
    /// Wire 순서대로 하나씩 <c>await</c>합니다. 대상마다 <see cref="DispatchOneAsync"/>가
    /// <c>msg.Clone()</c>으로 메시지를 통째로 복사해서 전달하므로, 한 노드가 <c>Payload</c>를 바꿔도 다른
    /// 분기에는 영향이 없습니다(2번 탭 카드2·3번 탭 카드4 데이터 격리 원칙). 아직 한 번도 배포된 적이
    /// 없거나(<see cref="_currentFlow"/>가 <c>null</c>) 대상 <see cref="NodeConfig.Id"/>가
    /// <see cref="Nodes"/>에 없으면(예: <see cref="MissingNode"/>로 남았거나 재배포로 제거된 경우) 해당
    /// 대상만 조용히 건너뜁니다.
    /// </summary>
    public async Task RouteAsync(string fromNodeId, int outputPort, Msg msg, CancellationToken ct)
    {
        if (msg.HopCount >= MaxHopCount)
        {
            // ★ RT-05: 카드2 원본은 여기서 FlowLoopGuardTrippedEvent를 발행하지만 EventBus(RT-07)가
            //   아직 없어 LoopGuardTrips 기록으로 대체(클래스 remarks·프로퍼티 문서 참고). 이 메시지 하나만
            //   버려지고 플로우 자체(다른 메시지·다른 노드)는 계속 정상 동작한다(카드2 원본 주석과 동일).
            _loopGuardTrips.Add((fromNodeId, msg.Id));
            return;
        }
        msg.HopCount++;

        if (_currentFlow is null) return;   // 아직 배포된 적 없음 — 참조할 Wire 정보가 없음

        var targets = _currentFlow.Wires
            .Where(w => w.SourceNodeId == fromNodeId && w.SourcePort == outputPort)
            .ToList();
        if (targets.Count == 0) return;

        // ★ RT-04b: IFlowNode에는 자신을 만든 NodeConfig에 대한 접근 수단이 없어(위 remarks 참고),
        //   발신 노드의 OutputDispatch는 _currentFlow.Nodes에서 별도로 조회한다. 찾지 못하면(예:
        //   RouteAsync를 배포되지 않은 Id로 직접 호출한 테스트 상황) Sequential로 안전하게 처리한다.
        var dispatch = _currentFlow.Nodes.FirstOrDefault(n => n.Id == fromNodeId)?.OutputDispatch ?? DispatchMode.Sequential;

        if (dispatch == DispatchMode.Parallel)
        {
            await Task.WhenAll(targets.Select(w => DispatchOneAsync(w, msg, ct)));
        }
        else
        {
            foreach (var wire in targets)
            {
                await DispatchOneAsync(wire, msg, ct);
            }
        }
    }

    /// <summary>
    /// <paramref name="wire"/>의 대상 노드 하나에게 <paramref name="msg"/>를 <c>Clone()</c>해 전달합니다
    /// (05번 탭 카드1 <c>DispatchOneAsync</c> 원본 + 카드3 <see cref="NodeExecutionGate"/> 동시성 제한,
    /// <c>RT-04b/RT-06</c>) — <see cref="RouteAsync"/>의 Sequential/Parallel 두 경로가 모두 이 메서드로
    /// 각 대상 전달을 위임해, 분기 방식과 무관하게 "대상마다 독립된 <see cref="Msg"/> 인스턴스를 받는다"는
    /// 격리 규칙이 항상 지켜집니다. (★ RT-06) 실제 <c>OnInputAsync</c> 호출 전에
    /// <see cref="NodeExecutionGate.GetGate"/>로 얻은 <see cref="SemaphoreSlim"/>을 통과해야 합니다 —
    /// 상한은 <see cref="_currentFlow"/>에서 찾은 대상 <see cref="NodeConfig.MaxConcurrency"/>를 우선 쓰고
    /// (사용자가 Editor에서 지정한 값), 배포 정보를 찾을 수 없으면 <see cref="IFlowNode.MaxConcurrency"/>
    /// 기본 구현(1)으로 대체합니다. 대상 <see cref="NodeConfig.Id"/>가 <see cref="Nodes"/>에 없으면 게이트를
    /// 거치지 않고 조용히 완료합니다.
    /// </summary>
    private async Task DispatchOneAsync(Wire wire, Msg msg, CancellationToken ct)
    {
        if (!_nodes.TryGetValue(wire.TargetNodeId, out var target)) return;

        // (LK-02a) _nodes에서 대상 인스턴스를 실제로 찾은 뒤에만 발행 — 배포에 아예 없는 대상(위
        // TryGetValue 실패)까지 "메시지가 흘렀다"고 캔버스에 알리면 사용자에게 거짓 정보가 되므로
        // 이 지점 이후에 발행한다. target이 MissingNode(등록되지 않은 타입의 자리표시자)여도 이미
        // _nodes에 들어 있어 이 줄까지 도달하므로 이벤트는 발행된다(의도된 동작 — 위 클래스 remarks
        // LK-02a 항목 참고). Publish(동기, IEventBus 계약)는 기존 NodeStatusEvent/DebugMessageEvent와
        // 동일한 방식이다(IEventBus에는 비동기 발행 오버로드가 없다).
        _eventBus.Publish(new FlowActivityEvent(wire.SourceNodeId, wire.SourcePort, wire.TargetNodeId, msg.Id, DateTime.UtcNow));

        var maxConcurrency = _currentFlow?.Nodes.FirstOrDefault(n => n.Id == wire.TargetNodeId)?.MaxConcurrency
            ?? target.MaxConcurrency;
        var gate = _gate.GetGate(wire.TargetNodeId, maxConcurrency);

        await gate.WaitAsync(ct);
        try
        {
            await target.OnInputAsync(msg.Clone(), BuildContext(wire.TargetNodeId, target), ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// (★ RT-03) <paramref name="a"/>와 <paramref name="b"/>가 "내용상 같은 설정"인지 필드 단위로 비교합니다.
    /// <c>NodeConfig.cs</c> remarks가 명시하는 대로 record 기본 <c>==</c>에 의존하지 않습니다 — <see cref="NodeConfig.Properties"/>는
    /// 딕셔너리 참조 비교가 아니라 키/값 내용 비교로 판정합니다(<see cref="NodeConfig.Id"/>는 이미 같은 Id끼리
    /// 비교하는 호출부 계약이라 비교 대상에서 제외).
    /// </summary>
    private static bool NodeConfigsDiffer(NodeConfig a, NodeConfig b)
    {
        if (a.Type != b.Type || a.Name != b.Name || a.FlowId != b.FlowId ||
            a.OutputDispatch != b.OutputDispatch || a.MaxConcurrency != b.MaxConcurrency ||
            a.CredentialRefId != b.CredentialRefId || a.Disabled != b.Disabled)
        {
            return true;
        }

        if (a.Properties.Count != b.Properties.Count) return true;

        foreach (var (key, value) in a.Properties)
        {
            if (!b.Properties.TryGetValue(key, out var otherValue)) return true;
            if (!Equals(value, otherValue)) return true;
        }

        return false;
    }

    /// <summary>
    /// <paramref name="nodeConfigId"/>/<paramref name="node"/>에 전달할 <see cref="INodeContext"/>를
    /// 만듭니다. 02번 문서 3번 탭 카드6·2번 탭 카드8에 호출부만 있고 정식 선언이 없던 <c>BuildContext</c>를
    /// <c>RT-01b</c>에서 <c>NoOpNodeContext</c>(임시 무동작 구현)로 처음 만들었고, (★ RT-09b) 이제 실제
    /// <see cref="NodeContext"/>(<see cref="NodeContext.Local"/>/<see cref="NodeContext.Flow"/>/
    /// <see cref="NodeContext.Global"/>/<see cref="NodeContext.Env"/> 4개 스코프 + <c>RouteAsync</c>/
    /// <c>SetStatus</c>)로 교체했습니다 — <c>NoOpNodeContext</c>는 더 이상
    /// 필요 없어 제거했습니다. <see cref="NodeContext"/>가 필요로 하는 <c>flowId</c>는
    /// <paramref name="nodeConfigId"/>로 <see cref="_currentFlow"/>.Nodes를 조회해 얻습니다(아직 배포된
    /// 적이 없거나 해당 Id를 찾지 못하면 빈 문자열 — <c>RT-04b</c>의 <c>OutputDispatch</c> 조회 실패 시
    /// 기본값 대체와 동일한 원칙). <paramref name="node"/>는 이 Step에서는 쓰이지 않지만, 노드 인스턴스별로
    /// 다른 Context가 필요해질 향후 확장을 대비해 시그니처를 유지합니다(<c>RT-04a</c> 당시와 동일한 판단).
    /// </summary>
    private INodeContext BuildContext(string nodeConfigId, IFlowNode node)
    {
        var flowId = _currentFlow?.Nodes.FirstOrDefault(n => n.Id == nodeConfigId)?.FlowId ?? string.Empty;
        return new NodeContext(this, _eventBus, _contextStore, flowId, nodeConfigId);
    }
}
