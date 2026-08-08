using NodeSharp.Contracts.Models;
using System.IO;

namespace NodeSharp.Editor.Core.Config;

/// <summary>
/// Class명 : 플로우 저장소
/// 역활 및 기능 : flows.json(Flow 탭 목록)을 <see cref="JsonWriteService"/>로 저장/로드하는 전용 창구
///
/// (EC-04) 캔버스(<c>FlowCanvasView</c>)가 직접 <see cref="JsonWriteService"/>의 파일 경로·직렬화
/// 옵션을 알 필요 없이, "이 Flow 탭 목록을 저장해줘/불러와줘"만 호출하면 되도록 감싼 얇은 래퍼입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>(EC-05 확장, ★ 사용자 요청, v2.51) 리스트 스키마</b>: flows.json의 최상위 형태는
/// <see cref="FlowDefinition"/> 목록(Flow 탭 개수만큼)입니다 — <see cref="FlowDefinition"/> 자신의
/// XML 문서가 애초에 이 형태를 명시하고 있었는데, EC-04 시점엔 Runner 쪽 <c>StartupSequencer</c>
/// (RN-01a)/<c>FlowDeployer</c>(RN-02)가 단일 객체로 읽도록 구현돼 있어 그 계약에 맞춰 단일
/// 스키마로 임시 구현했었습니다. EC-05(다중 Flow 탭)에서 이 불일치를 발견해 사용자 확인 후
/// (① 리스트 스키마로 전환 선택) 정식으로 리스트로 바꾸고, <c>StartupSequencer</c>/<c>FlowDeployer</c>도
/// 함께 리스트를 읽도록 수정했습니다(<c>NodeSharp.Runner.csproj</c> EC-05 블록 참고). <c>FlowEngine</c>
/// (Runtime, <c>RT-0x</c>) 자체는 전혀 수정하지 않았습니다 — <c>FlowDeployer</c>가 활성 탭들을
/// 하나로 병합해 기존 단일-<see cref="FlowDefinition"/> API를 그대로 호출합니다.</item>
/// <item><b>LK-01과의 경계</b>: 이 클래스는 저장 후 <c>.signal</c> 파일까지만 남깁니다(신호를
/// "보내는" 쪽). Runner가 <c>FileSystemWatcher</c>로 그 신호를 "감지"해서 실제로 재배포하는 로직은
/// Phase 8의 LK-01 범위이며, 이 Step(EC-04)의 완료 기준에서 그 부분은 제외됩니다.</item>
/// <item><b>WPF 프로젝트라 샌드박스에서 테스트 불가</b>: 이 클래스는 순수 System.IO 로직이지만
/// <c>NodeSharp.Editor</c> 프로젝트(net8.0-windows, UseWPF=true) 소속이라 리눅스 샌드박스에서는
/// 빌드·테스트가 불가능합니다(기존 <c>PaletteRecentUsageTracker.cs</c>와 동일한 제약). 실제 동작
/// 확인은 사용자가 Windows에서 직접 빌드·실행해 확인해야 합니다.</item>
/// </list>
/// </remarks>
public sealed class FlowStore
{
    private const string FileName = "flows.json";

    /// <summary>
    /// <paramref name="flows"/>(Flow 탭 목록)를 <paramref name="dataDirectory"/>\flows.json에
    /// 원자적으로 저장하고 .signal 파일을 남깁니다.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<FlowDefinition> flows, string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        await JsonWriteService.WriteAtomicAsync(path, flows, ct);
        await JsonWriteService.WriteSignalAsync(path, ct);
    }

    /// <summary>
    /// <paramref name="dataDirectory"/>\flows.json을 읽어 <see cref="FlowDefinition"/> 목록(Flow 탭
    /// 목록)으로 반환합니다. 파일이 없으면(최초 실행) <c>null</c>을 반환합니다.
    /// </summary>
    public async Task<List<FlowDefinition>?> LoadAsync(string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        return await JsonWriteService.ReadAsync<List<FlowDefinition>>(path, ct);
    }
}
