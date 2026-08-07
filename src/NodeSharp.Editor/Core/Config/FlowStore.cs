using NodeSharp.Contracts.Models;
using System.IO;

namespace NodeSharp.Editor.Core.Config;

/// <summary>
/// Class명 : 플로우 저장소
/// 역활 및 기능 : flows.json 하나를 <see cref="JsonWriteService"/>로 저장/로드하는 전용 창구
///
/// (EC-04) 캔버스(<c>FlowCanvasView</c>)가 직접 <see cref="JsonWriteService"/>의 파일 경로·직렬화
/// 옵션을 알 필요 없이, "이 FlowDefinition을 저장해줘/불러와줘"만 호출하면 되도록 감싼 얇은 래퍼입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>단일 FlowDefinition 스키마</b>: Runner 쪽 <c>StartupSequencer</c>(RN-01a, 이미 완료)가
/// flows.json을 <c>JsonSerializer.Deserialize&lt;FlowDefinition&gt;</c>로, 즉 리스트가 아니라
/// 단일 객체로 읽도록 이미 구현·확인되어 있습니다. Editor 쪽도 반드시 같은 스키마(탭 1개 = 파일 1개
/// 아니라, 지금 범위에서는 "플로우 1개 = flows.json 1개")로 맞춰야 하며, 여러 탭을 지원하는 것은
/// 이후 별도 Step 범위입니다.</item>
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
    /// <paramref name="flow"/>를 <paramref name="dataDirectory"/>\flows.json에 원자적으로 저장하고
    /// .signal 파일을 남깁니다.
    /// </summary>
    public async Task SaveAsync(FlowDefinition flow, string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        await JsonWriteService.WriteAtomicAsync(path, flow, ct);
        await JsonWriteService.WriteSignalAsync(path, ct);
    }

    /// <summary>
    /// <paramref name="dataDirectory"/>\flows.json을 읽어 <see cref="FlowDefinition"/>으로 반환합니다.
    /// 파일이 없으면(최초 실행) <c>null</c>을 반환합니다.
    /// </summary>
    public async Task<FlowDefinition?> LoadAsync(string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        return await JsonWriteService.ReadAsync<FlowDefinition>(path, ct);
    }
}
