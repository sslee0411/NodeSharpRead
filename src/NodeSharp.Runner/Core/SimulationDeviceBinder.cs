using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Drivers.Modbus;
using NodeSharp.Runtime;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 시뮬레이션 디바이스 배선기
/// 역활 및 기능 : CurrentDeviceTreeHolder가 들고 있는 device.json(DeviceTreeDto)에서
/// simulationMode=true인 PlcNode를 찾아 VirtualModbusSlave를 만들고, 그 아래 DeviceMap/Tag 구조를
/// 그대로 DeviceMapPoller들로 변환하는 정적 도우미
///
/// (PD-01e, ★ 신규) 사용자가 확인한 4단계 범위("전체 4단계 범위 그대로 구현", 2026-09-02) 중
/// "device.json 파싱 + 드라이버/슬레이브 배선" 부분을 담당합니다. <c>IStructureService</c>(CT-04b)가
/// 아직 구현되어 있지 않아(ED-D04/ED-D06b와 동일한 상황), <see cref="DeviceTreeDto"/>를 이 클래스가
/// 직접 순회합니다 — <c>StructureTreeMapper</c>(Editor)가 device.json에 쓸 때와 정확히 반대 방향의
/// 변환이며, <see cref="StructureTreeNodeDto.NodeType"/> 문자열("Device"/"Plc"/"DeviceMap"/"Tag")과
/// <see cref="StructureTreeNodeDto.Properties"/> 키(PropertyField.Key, 예: "simulationMode"/
/// "startAddress"/"offset")는 <c>StructureTreeMapper.TypeNames</c>/각 노드 클래스의 <c>PropertySchema</c>와
/// 반드시 일치해야 합니다(문자열로만 연결된 계약이라 컴파일 타임 보증이 없음 — 이 클래스가 깨지면
/// device.json 스키마가 바뀐 것이 원인일 가능성이 가장 큽니다).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>레지스터 주소 변환(범위 축소, ED-D06b/PD-01c와 동일한 원칙)</b>: <c>TagNode.Offset</c>은
/// "디바이스맵 시작 주소로부터 몇 바이트 떨어졌는지"(byte 단위)이지만,
/// <see cref="VirtualModbusSlave.GetRegister(int)"/>는 레지스터 단위(2바이트)로 주소를 받습니다.
/// <c>BufferParser</c>(바이트 단위 실제 파싱)가 이 코드베이스 어디에도 없어(PD-01e 착수 전 조사 확인),
/// 이 클래스는 <c>registerAddress = deviceMap.StartAddress + (tag.Offset / 2)</c>로 단순 변환하고,
/// 항상 레지스터 1개(UInt16)만 읽습니다 — <c>TagNode.BufType</c>(FloatLE/Int32 등 여러 레지스터에
/// 걸치는 타입)은 아직 반영하지 않습니다(후속 Step에서 BufferParser가 생기면 이 메서드 안에서만
/// 교체하면 되도록 <see cref="BindResult.Pollers"/> 반환 형태를 이미 <see cref="DeviceMapPoller"/>
/// 단위로 캡슐화해 뒀습니다).</item>
/// <item><b><see cref="Bind"/>는 새로 만들 뿐 시작하지 않음</b>: 반환된 <see cref="DeviceMapPoller"/>의
/// <c>StartAsync</c> 호출과 이전 배선의 <c>StopAsync</c> 정리는 호출부(<c>Worker</c>)의 책임입니다 —
/// 이 클래스는 순수 변환만 담당해 테스트하기 쉽게 유지합니다(<c>StructureTreeMapper</c>가 저장/복원만
/// 하고 UI를 모르는 것과 동일한 책임 분리).</item>
/// </list>
/// </remarks>
public static class SimulationDeviceBinder
{
    /// <summary>
    /// <paramref name="deviceTree"/>(<c>null</c>이면 device.json이 아직 없음 — 빈 결과)를 순회해
    /// simulationMode=true인 PlcNode마다 새 <see cref="VirtualModbusSlave"/>를 만들어
    /// <paramref name="slaveHolder"/>에 등록하고(먼저 <see cref="SimulationSlaveHolder.Clear"/>로
    /// 이전 배선을 지움 — 구조 재로드 시 더 이상 시뮬레이션 모드가 아니게 된 PLC가 남지 않도록), 그
    /// 아래 DeviceMap/Tag 구조를 <paramref name="tagValueCache"/>/<paramref name="eventBus"/>를
    /// 공유하는 <see cref="DeviceMapPoller"/> 목록으로 변환해 반환합니다.
    /// </summary>
    public static IReadOnlyList<DeviceMapPoller> Bind(
        DeviceTreeDto? deviceTree,
        SimulationSlaveHolder slaveHolder,
        TagValueCache tagValueCache,
        IEventBus eventBus)
    {
        slaveHolder.Clear();
        var pollers = new List<DeviceMapPoller>();

        if (deviceTree is null)
        {
            return pollers;
        }

        foreach (var deviceDto in deviceTree.Devices.Where(d => d.NodeType == "Device"))
        {
            foreach (var plcDto in deviceDto.Children.Where(p => p.NodeType == "Plc"))
            {
                if (!IsSimulationMode(plcDto))
                {
                    continue;
                }

                var slave = new VirtualModbusSlave();
                slaveHolder.Register(plcDto.Id, slave);

                foreach (var mapDto in plcDto.Children.Where(m => m.NodeType == "DeviceMap"))
                {
                    var poller = BuildPoller(mapDto, slave, tagValueCache, eventBus);
                    if (poller is not null)
                    {
                        pollers.Add(poller);
                    }
                }
            }
        }

        return pollers;
    }

    /// <summary>디바이스맵 1개(및 그 아래 Tag들)를 태그가 하나도 없으면 <c>null</c>, 있으면 <see cref="DeviceMapPoller"/> 1개로 변환합니다 — 클래스 remarks의 "레지스터 주소 변환" 항목 참고.</summary>
    private static DeviceMapPoller? BuildPoller(StructureTreeNodeDto mapDto, VirtualModbusSlave slave, TagValueCache tagValueCache, IEventBus eventBus)
    {
        var startAddress = GetInt(mapDto, "startAddress");
        var tagDtos = mapDto.Children.Where(t => t.NodeType == "Tag").ToList();
        if (tagDtos.Count == 0)
        {
            return null;
        }

        var tagIds = tagDtos.Select(t => t.Id).ToList();
        var registerAddresses = tagDtos.ToDictionary(t => t.Id, t => startAddress + (GetInt(t, "offset") / 2));

        return new DeviceMapPoller
        {
            Id = mapDto.Id,
            TagIds = tagIds,
            Cache = tagValueCache,
            EventBus = eventBus,
            BlockReadAction = _ =>
            {
                var values = new Dictionary<string, object?>();
                foreach (var tagId in tagIds)
                {
                    values[tagId] = (int)slave.GetRegister(registerAddresses[tagId]);
                }

                return Task.FromResult<IReadOnlyDictionary<string, object?>>(values);
            },
        };
    }

    /// <summary><paramref name="plcDto"/>.Properties["simulationMode"]가 "True"(대소문자 무시, <c>StructureTreeMapper.ToNodeDto</c>가 <c>bool.ToString()</c>으로 쓴 값)면 <c>true</c>입니다. 키가 없거나 파싱할 수 없으면(구버전 device.json 등) <c>false</c>로 취급합니다.</summary>
    private static bool IsSimulationMode(StructureTreeNodeDto plcDto) =>
        plcDto.Properties.TryGetValue("simulationMode", out var raw) && bool.TryParse(raw, out var value) && value;

    /// <summary><paramref name="dto"/>.Properties[<paramref name="key"/>]를 정수로 파싱합니다. 없거나 파싱 실패하면 0입니다(<c>StructureTreeMapper.TrySetTypedValue</c>의 "변환 실패는 조용히 건너뜀"과 동일한 관용).</summary>
    private static int GetInt(StructureTreeNodeDto dto, string key) =>
        dto.Properties.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : 0;
}
