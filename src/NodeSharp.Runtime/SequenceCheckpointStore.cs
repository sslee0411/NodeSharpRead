using System.Text.Json;
using NodeSharp.Contracts.Enums;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 시퀀스 체크포인트 저장소
/// 역활 및 기능 : SequenceExecutor의 단계 전환마다 원자적으로 기록해, Runner 크래시/재기동 시 진행 중이던 단계를 복구할 수 있게 하는 저장소
///
/// (SQ-05) 02번 설계 문서 8번 탭 "★ 크래시 복구(시스템 공백)" 항목이 발견한 공백 — "<c>SequenceExecutor.CurrentStepId</c>가
/// 메모리에만 있어 Runner가 크래시/재기동하면 진행 중이던 단계를 알 수 없었다"를 메우는 클래스입니다.
/// 문서가 지정한 파일명 <c>sequences.checkpoint.json</c>(sequences.json과는 별도 파일 — 자주 갱신되므로
/// 원본과 분리, 10번 탭 다세대 백업 대상에서는 제외)을 그대로 씁니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Editor의 <c>JsonWriteService</c>를 재사용하지 않는 이유</b>: 이 클래스는
/// <c>NodeSharp.Runtime</c>(순수 <c>net8.0</c>) 소속이라 Runner(headless)에서도 쓰입니다 —
/// <c>NodeSharp.Editor.Core.Config.JsonWriteService</c>는 <c>NodeSharp.Editor</c>(net8.0-windows,
/// UseWPF=true) 소속이라 여기서 참조하면 계층 규칙(Runtime → Editor 역참조 금지)을 어기게 됩니다.
/// 그래서 <see cref="JsonWriteService"/>와 같은 "임시파일 → 원자적 교체" 알고리즘을
/// <see cref="WriteAllAsync"/>에 그대로 다시 구현했습니다(의도적 중복 — 각자의 계층에서 독립적으로
/// 존재해야 함).</item>
/// <item><b>스키마 — 여러 시퀀스를 한 파일에</b>: 문서가 파일명을 단수(<c>sequences.checkpoint.json</c>)로
/// 못박아 하나의 파일에 여러 시퀀스의 체크포인트를 함께 저장합니다(<see cref="SaveAsync"/>가
/// <see cref="Checkpoint.SequenceId"/> 기준으로 기존 항목을 교체하거나 새로 추가). 시퀀스 개수가
/// 많지 않을 것으로 보고(설비 기동/정지 절차 단위) 매 저장마다 파일 전체를 다시 읽고 쓰는 가장 단순한
/// 형태로 시작했습니다 — <c>SequenceStore.OnSaveClick</c>(SQ-04)이 sequences.json에 쓰는 것과 동일한
/// "먼저 전체를 읽어 병합" 원칙입니다.</item>
/// <item><b>WPF 없이 테스트 가능</b>: <c>FlowStore</c>/<c>SequenceStore</c>(Editor)와 달리 이 클래스는
/// 순수 <c>net8.0</c> 프로젝트 소속이라 샌드박스에서도 xUnit으로 직접 검증할 수 있습니다
/// (<c>SequenceCheckpointStoreTests.cs</c> 참고).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var store = new SequenceCheckpointStore();
/// await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-1", "밸브 열기", SequenceState.Running, DateTime.UtcNow), dataDirectory);
/// var found = await store.TryLoadAsync("seq-1", dataDirectory);   // 방금 저장한 값 그대로
/// </code>
/// </example>
public sealed class SequenceCheckpointStore
{
    private const string FileName = "sequences.checkpoint.json";

    /// <summary>
    /// 시퀀스 하나의 체크포인트 스냅샷 — 02번 설계 문서 <c>SequenceCheckpointStore</c> 의사코드의
    /// <c>Checkpoint</c> 레코드 그대로입니다.
    /// </summary>
    /// <param name="SequenceId"><see cref="Contracts.Models.SequenceDefinition.Id"/>.</param>
    /// <param name="CurrentStepId">기록 시점의 <c>SequenceExecutor.CurrentStepName</c>(단계 이름).</param>
    /// <param name="State">기록 시점의 <see cref="SequenceState"/>.</param>
    /// <param name="At">기록 시각(UTC).</param>
    public sealed record Checkpoint(string SequenceId, string CurrentStepId, SequenceState State, DateTime At);

    /// <summary>
    /// <paramref name="checkpoint"/>를 <paramref name="dataDirectory"/>\sequences.checkpoint.json에
    /// 원자적으로 기록합니다 — 같은 <see cref="Checkpoint.SequenceId"/>의 기존 항목이 있으면 교체하고,
    /// 없으면 추가합니다(다른 시퀀스의 체크포인트는 그대로 유지).
    /// </summary>
    public async Task SaveAsync(Checkpoint checkpoint, string dataDirectory, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(dataDirectory, ct);
        var index = all.FindIndex(c => c.SequenceId == checkpoint.SequenceId);
        if (index >= 0)
        {
            all[index] = checkpoint;
        }
        else
        {
            all.Add(checkpoint);
        }

        await WriteAllAsync(all, dataDirectory, ct);
    }

    /// <summary>
    /// <paramref name="sequenceId"/>의 체크포인트를 찾아 반환합니다. 파일이 없거나 그 Id의 항목이
    /// 없으면 <c>null</c>을 반환합니다(예외 아님 — "체크포인트 없음"은 정상적인 최초 실행 상태).
    /// </summary>
    public async Task<Checkpoint?> TryLoadAsync(string sequenceId, string dataDirectory, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(dataDirectory, ct);
        return all.FirstOrDefault(c => c.SequenceId == sequenceId);
    }

    /// <summary>
    /// <paramref name="sequenceId"/>의 체크포인트를 제거합니다("처음부터" 선택 시 —
    /// <c>MonitorHub.ResolveSequenceCheckpoint</c>가 호출) — 다른 시퀀스의 체크포인트는 그대로 둡니다.
    /// 해당 Id가 없으면 아무 것도 하지 않습니다.
    /// </summary>
    public async Task ClearAsync(string sequenceId, string dataDirectory, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(dataDirectory, ct);
        var removed = all.RemoveAll(c => c.SequenceId == sequenceId);
        if (removed > 0)
        {
            await WriteAllAsync(all, dataDirectory, ct);
        }
    }

    /// <summary><paramref name="dataDirectory"/>\sequences.checkpoint.json 전체를 읽습니다. 파일이 없으면 빈 목록(최초 실행).</summary>
    private static async Task<List<Checkpoint>> LoadAllAsync(string dataDirectory, CancellationToken ct)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
        {
            return new List<Checkpoint>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<Checkpoint>>(json) ?? new List<Checkpoint>();
        }
        catch (JsonException)
        {
            // (JsonWriteService.ReadAsync와 동일한 "격리" 원칙) 손상된 파일은 "체크포인트 없음"과
            // 동일하게 취급 — 크래시 복구 기능 자체가 손상된 상태 파일 때문에 Runner 기동을 막으면 안 됨.
            return new List<Checkpoint>();
        }
    }

    /// <summary>(JsonWriteService.WriteAtomicAsync와 동일한 알고리즘의 자체 구현 — 클래스 자체 주석 "Editor의 JsonWriteService를 재사용하지 않는 이유" 참고) 임시파일에 전부 쓴 뒤 원자적으로 교체합니다.</summary>
    private static async Task WriteAllAsync(List<Checkpoint> all, string dataDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(dataDirectory);

        var path = Path.Combine(dataDirectory, FileName);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempPath, json, ct);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, path + ".bak");
        }
        else
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
