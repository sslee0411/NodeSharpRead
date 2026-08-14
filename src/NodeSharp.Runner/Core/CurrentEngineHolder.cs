using NodeSharp.Runtime;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 현재 엔진 홀더
/// 역활 및 기능 : 지금 이 Runner 프로세스가 실행 중인 FlowEngine 인스턴스를 가리키는 얇은 공유 홀더
///
/// (LK-02b 후속, 사용자 요청 — "Inject 노드를 클릭/버튼으로 트리거") <c>MonitorHub</c>(SignalR Hub)는
/// 클라이언트가 호출할 때마다 DI가 새로 만드는 인스턴스라 <c>Worker.ExecuteAsync</c>의 지역 변수
/// <c>engine</c>에 직접 접근할 방법이 없습니다 — 이 클래스가 그 접근 통로입니다. <c>Worker</c>가 배포/
/// 재배포에 성공할 때마다(<see cref="StatusBroadcaster"/>가 이벤트를 중계받는 것과 별개로) 최신
/// <see cref="FlowEngine"/>을 이 홀더에 기록해두면, <c>MonitorHub.TriggerInject</c>가 이 홀더를 통해
/// "지금 배포된 엔진"의 <see cref="FlowEngine.TriggerManualAsync"/>를 호출할 수 있습니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>단일 엔진 전제</b>: 이 프로젝트는 Runner 프로세스 하나가 항상 <see cref="FlowEngine"/>
/// 인스턴스 하나만 운영합니다(<c>LK-01</c>의 "엔진 재사용" 설계, 여러 Flow 탭은 이 엔진 하나로 병합돼
/// 배포됨, <c>FlowDeployer.MergeActiveFlows</c> 참고) — 그래서 "현재 엔진" 개념이 모호하지 않고
/// 프로퍼티 하나로 충분합니다.</item>
/// <item><b>스레드 안전성</b>: <see cref="Engine"/>은 <c>Worker</c>(배포 시)와 SignalR Hub 호출
/// 스레드(조회 시) 양쪽에서 접근되지만, 참조 타입 프로퍼티의 단순 대입/읽기는 .NET에서 원자적이라
/// (torn read가 없음) 별도 락 없이도 안전합니다 — 다만 "읽은 시점"과 "실제로 그 엔진에 트리거를
/// 보내는 시점" 사이에 재배포가 끼어들면 트리거가 방금 교체되기 직전 엔진으로 갈 수 있습니다(드문
/// 경합, 최악의 경우 트리거가 무시되는 정도이고 예외는 나지 않음 — 완료 기준 범위 밖의 미세한 경합).</item>
/// </list>
/// </remarks>
public sealed class CurrentEngineHolder
{
    /// <summary>지금 배포된 <see cref="FlowEngine"/>. 아직 한 번도 배포된 적이 없으면 <c>null</c>입니다.</summary>
    public FlowEngine? Engine { get; set; }
}
