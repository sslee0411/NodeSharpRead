using NodeSharp.Contracts.Models;
using System.IO;

namespace NodeSharp.Editor.Core.Config;

/// <summary>
/// Class명 : 구조 설정 트리 저장소
/// 역활 및 기능 : device.json(구조 설정 6단계 트리)을 <see cref="JsonWriteService"/>로 저장/로드하는
/// 전용 창구 — <see cref="FlowStore"/>(flows.json)와 완전히 동일한 얇은 래퍼 패턴입니다.
///
/// (ED-D03) 02번 설계문서 8번 탭 카드6 "저장 포맷 및 Runner 연동"의 "원자적 저장" 요구를
/// <see cref="JsonWriteService.WriteAtomicAsync{T}"/>(EC-04에서 flows.json에 이미 검증된 .tmp→
/// <see cref="File.Replace(string, string, string?)"/> 패턴)를 그대로 재사용해 만족합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>"라이브러리성 데이터 분리 저장" 범위 제외</b>: 03번 Step맵의 ED-D03 설명은 "공용 스케일
/// 등 라이브러리성 데이터는 별도 파일로 분리 저장"이라고 적혀 있지만, 02번 설계문서 8번 탭 카드6은
/// <c>DeviceTreeDto(IReadOnlyList&lt;StructureTreeNodeDto&gt; Devices)</c> 단일 파일 저장만 정의하고
/// 있고, "라이브러리 파일" 자체의 스키마·경로·분리 기준은 이 프로젝트 어디에도(카드6/카드7 포함)
/// 정의돼 있지 않습니다(유일한 관련 언급은 iiot-system-arch 스킬의 S-06/S-07을 참조로 든 각주뿐 —
/// 그 스킬의 "공용 스케일/알람 라이브러리" 개념을 그대로 차용한 것인지, 이 프로젝트가 실제로
/// 채택한 설계인지 불명확). 이 프로젝트의 <c>ScaleNode</c>/<c>AlarmNode</c>(ED-D01)는 애초에
/// 태그마다 딸린 자식 노드로 설계돼 있어(공유 참조가 아님) "분리 저장할 라이브러리"가 현재 트리
/// 구조상 존재하지 않습니다. 따라서 이 Step은 실제로 명세된 <c>DeviceTreeDto</c> 단일 파일 원자적
/// 저장만 구현하고, 라이브러리 분리는 근거 없는 상태로 남겨 Step맵/README에 그대로 표시합니다 —
/// ED-D02a의 "그룹" 표기·ED-D02b의 <c>AlarmStateManager</c> 부재와 동일하게 다룬 처리입니다.</item>
/// <item><b>Runner 쪽 소비(<c>IStructureService</c>, 카드7)는 이 Step 범위 밖</b>: 카드7이 정의하는
/// <c>IStructureService</c>/<c>TagRuntimeInfo</c>/<c>StructureService</c> 등은 Runner(헤드리스,
/// WPF 비의존)가 <see cref="DeviceTreeDto"/>를 읽어 실제 통신에 쓰는 다음 단계 설계입니다 — 이
/// Step(ED-D03)은 "Editor가 device.json을 손상 없이 저장/로드할 수 있는지"만 다루고, Runner가 그
/// 파일을 읽어 쓰는 부분은 별도 Step(설계상 ED-D04 TagRef 연동 또는 그 이후, PD-01a Modbus TCP
/// 드라이버가 선행돼야 함)에서 구현합니다.</item>
/// </list>
/// </remarks>
public sealed class DeviceStore
{
    private const string FileName = "device.json";

    /// <summary>
    /// <paramref name="tree"/>(구조 설정 트리 DTO)를 <paramref name="dataDirectory"/>\device.json에
    /// 원자적으로 저장하고 .signal 파일을 남깁니다.
    /// </summary>
    public async Task SaveAsync(DeviceTreeDto tree, string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        await JsonWriteService.WriteAtomicAsync(path, tree, ct);
        await JsonWriteService.WriteSignalAsync(path, ct);
    }

    /// <summary>
    /// <paramref name="dataDirectory"/>\device.json을 읽어 <see cref="DeviceTreeDto"/>로 반환합니다.
    /// 파일이 없으면(최초 실행) <c>null</c>을 반환합니다.
    /// </summary>
    public async Task<DeviceTreeDto?> LoadAsync(string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        return await JsonWriteService.ReadAsync<DeviceTreeDto>(path, ct);
    }
}
