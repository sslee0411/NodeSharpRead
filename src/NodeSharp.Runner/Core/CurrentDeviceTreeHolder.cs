using NodeSharp.Contracts.Models;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 현재 구조 트리 홀더
/// 역활 및 기능 : 지금 이 Runner 프로세스가 마지막으로 읽은 device.json(구조 설정 트리) 내용을 가리키는 얇은 공유 홀더
///
/// (ED-D13, ★ 완료 기준 — "구조만 바뀌면 ReloadStructureOnlyAsync로 가볍게, Flow도 바뀌면 기존
/// DeployAsync까지 수행") <see cref="IStructureService"/>(CT-04b가 선언만 해둔 Contracts 인터페이스 —
/// ED-D12 조사에서 실제 구현체가 어디에도 없음을 재확인)의 진짜 구현체가 생기기 전까지, 이 홀더가
/// "Runner가 마지막으로 읽은 구조 트리"를 담아두는 임시 자리입니다 — <see cref="CurrentEngineHolder"/>
/// (LK-02b 후속)와 완전히 동일한 "얇은 공유 홀더" 패턴을 그대로 재사용했습니다. 지금 당장은
/// <see cref="StructureReloader"/> 하나만 이 값을 씁니다(써넣기만 하고 아무도 읽지 않음)지만, 실제
/// PLC 연결·태그 조회 로직(<c>IStructureService</c> 구현체가 생기는 후속 Step)이 이 홀더를 읽어가는
/// 방식으로 자연스럽게 이어질 수 있도록 미리 만들어 뒀습니다.
/// </summary>
/// <remarks>
/// <see cref="CurrentEngineHolder"/>와 마찬가지로 참조 타입 프로퍼티의 단순 대입/읽기는 .NET에서
/// 원자적이라 별도 락 없이 안전합니다 — 지금은 <c>Worker.ExecuteAsync</c>(쓰기)만 접근하므로 경합
/// 자체가 없습니다.
/// </remarks>
public sealed class CurrentDeviceTreeHolder
{
    /// <summary>지금 Runner가 마지막으로 읽은 device.json 내용. 한 번도 읽은 적이 없으면(파일이 아직 없는 경우 포함) <c>null</c>입니다.</summary>
    public DeviceTreeDto? DeviceTree { get; set; }
}
