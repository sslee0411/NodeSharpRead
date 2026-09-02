using NodeSharp.Drivers.Modbus;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 시뮬레이션 슬레이브 홀더
/// 역활 및 기능 : 지금 이 Runner 프로세스가 시뮬레이션 모드로 소유 중인 PLC별 VirtualModbusSlave를
/// PlcNode.Id 기준으로 보관하는 얇은 공유 홀더
///
/// (PD-01e, ★ 신규) 사용자 확인("Runner로 이전 + SignalR 원격제어", 2026-09-02)에 따라, PD-01d까지
/// Editor(SimulatorPanelView)가 소유하던 <see cref="VirtualModbusSlave"/> 인스턴스를 이제 Runner가
/// 직접 소유합니다 — Editor와 Runner는 별도 프로세스라 인메모리 상태를 공유할 수 없고(이 발견이 이
/// Step의 재설계 원인), Editor는 이제 값을 "직접 들고" 있지 않고 SignalR로 <c>MonitorHub.SetSimulatedRegister</c>를
/// 호출해 이 홀더가 가진 슬레이브에 원격으로 씁니다(<see cref="CurrentEngineHolder"/>/
/// <see cref="CurrentDeviceTreeHolder"/>와 완전히 동일한 "얇은 공유 홀더" 패턴 — 한쪽(<c>SimulationDeviceBinder</c>)이
/// 쓰고, 다른 쪽(<c>MonitorHub</c>)이 읽습니다).
/// </summary>
/// <remarks>
/// <see cref="CurrentEngineHolder"/> 클래스 문서와 동일한 이유로 별도 락이 필요 없습니다 —
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>를 써서 여러 스레드
/// (<c>Worker</c>의 초기 배선, <c>MonitorHub</c>의 SignalR 호출 스레드)가 동시에 접근해도 안전합니다.
/// </remarks>
public sealed class SimulationSlaveHolder
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VirtualModbusSlave> _slaves = new();

    /// <summary>
    /// <paramref name="plcId"/>(PlcNode.Id)에 대응하는 <see cref="VirtualModbusSlave"/>를 등록합니다.
    /// <see cref="SimulationDeviceBinder"/>가 device.json을 읽어 시뮬레이션 모드 PLC마다 슬레이브를
    /// 만들 때 호출합니다. 이미 등록된 Id면 새 인스턴스로 교체합니다(구조 재로드 시나리오).
    /// </summary>
    public void Register(string plcId, VirtualModbusSlave slave) => _slaves[plcId] = slave;

    /// <summary>
    /// <paramref name="plcId"/>에 등록된 <see cref="VirtualModbusSlave"/>를 반환합니다. 등록된 적이
    /// 없으면(시뮬레이션 모드가 아닌 PLC, 잘못된 Id 등) <c>null</c>입니다 —
    /// <see cref="MonitorHub.SetSimulatedRegister"/>가 이 값을 확인해 조용히 무시할지 판단합니다.
    /// </summary>
    public VirtualModbusSlave? TryGet(string plcId) => _slaves.TryGetValue(plcId, out var slave) ? slave : null;

    /// <summary>구조 재로드(device.json.signal) 시 <see cref="SimulationDeviceBinder"/>가 이전 배선을 모두 지우고 새로 만들기 전에 호출합니다 — 더 이상 시뮬레이션 모드가 아니게 된 PLC의 슬레이브가 남아있지 않도록 합니다.</summary>
    public void Clear() => _slaves.Clear();
}
