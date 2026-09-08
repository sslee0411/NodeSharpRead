using NodeSharp.Contracts.Models;
using System.IO;

namespace NodeSharp.Editor.Core.Config;

/// <summary>
/// Class명 : 시퀀스 저장소
/// 역활 및 기능 : sequences.json(Sequence Editor가 다루는 SequenceDefinition 목록)을 JsonWriteService로 저장/로드하는 전용 창구
///
/// (SQ-04) <see cref="FlowStore"/>(EC-04, flows.json)와 완전히 동일한 얇은 래퍼 패턴입니다 —
/// <see cref="SequenceEditorWindow"/>가 <see cref="JsonWriteService"/>의 파일 경로·직렬화 옵션을 직접
/// 알 필요 없이 "이 시퀀스 목록을 저장해줘/불러와줘"만 호출하면 되도록 감쌌습니다. 02번 설계 문서가
/// 처음부터 sequences.json을 <see cref="JsonWriteService"/> 제네릭 재사용 대상으로 명시해뒀던 것을
/// 그대로 따릅니다(<see cref="JsonWriteService"/> 클래스 자체 주석 참고).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>스키마</b>: sequences.json의 최상위 형태는 <see cref="SequenceDefinition"/> 목록입니다
/// (한 파일에 여러 시퀀스를 함께 저장 — flows.json이 <c>FlowDefinition</c> 목록인 것과 동일한 구조).</item>
/// <item><b>LK-01과의 경계</b>: <see cref="FlowStore"/>와 동일하게, 이 클래스는 저장 후 <c>.signal</c>
/// 파일까지만 남깁니다 — Runner가 FileSystemWatcher로 감지해 재로드하는 로직은 Phase 8 LK-01 범위.</item>
/// <item><b>(SQ-04, ★ 범위 축소) ED-D13 더티 비교 미적용</b>: <c>FlowCanvasView.SaveFlowAsync</c>가
/// ED-D13에서 추가한 "마지막 저장 내용과 다를 때만 실제로 쓴다" 최적화는 이 최초 구현에는
/// 없습니다(EC-04 최초 <see cref="FlowStore"/>/<c>SaveFlowAsync</c>도 이 최적화 없이 시작했었고,
/// ED-D13에서 나중에 추가된 것과 동일한 순서) — 필요해지면 <see cref="SequenceEditorWindow"/>에도
/// 동일한 패턴으로 나중에 추가할 수 있습니다.</item>
/// <item><b>WPF 프로젝트라 샌드박스에서 테스트 불가</b>: <see cref="FlowStore"/>와 동일한 이유
/// (net8.0-windows, UseWPF=true) — 실제 동작 확인은 사용자가 Windows에서 직접 빌드·실행해 확인해야
/// 합니다.</item>
/// </list>
/// </remarks>
public sealed class SequenceStore
{
    private const string FileName = "sequences.json";

    /// <summary>
    /// <paramref name="sequences"/>(전체 SequenceDefinition 목록)를 <paramref name="dataDirectory"/>\sequences.json에
    /// 원자적으로 저장하고 .signal 파일을 남깁니다.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<SequenceDefinition> sequences, string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        await JsonWriteService.WriteAtomicAsync(path, sequences, ct);
        await JsonWriteService.WriteSignalAsync(path, ct);
    }

    /// <summary>
    /// <paramref name="dataDirectory"/>\sequences.json을 읽어 <see cref="SequenceDefinition"/> 목록으로
    /// 반환합니다. 파일이 없으면(최초 실행) <c>null</c>을 반환합니다.
    /// </summary>
    public async Task<List<SequenceDefinition>?> LoadAsync(string dataDirectory, CancellationToken ct = default)
    {
        var path = Path.Combine(dataDirectory, FileName);
        return await JsonWriteService.ReadAsync<List<SequenceDefinition>>(path, ct);
    }
}
