using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// 장비→PLC→디바이스맵→태그→스케일→알람 6단계 구조 설정을 헤드리스 Runner가 순수 데이터로만
/// 읽고 쓰는 계약입니다. Editor 전용 <c>StructureTreeNode</c>(<c>ObservableCollection</c> 기반, WPF
/// 의존)를 Runner가 직접 참조하던 계층 위반을 없애기 위해 도입되었습니다.
/// 설계 근거: 02번 문서 8번 탭 카드 7·13.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>구현체(<c>StructureService</c>, NodeSharp.Runtime)는 device.json을 로드해 이 인터페이스가
/// 요구하는 형태로 평면화(Dictionary 인덱스)해 둡니다 — Editor의 트리 구조를 그대로 순회하지 않습니다.</item>
/// <item><see cref="FindNodesByTagRef"/>는 "태그 삭제 가능 여부 확인"과 "캔버스 역방향 하이라이트
/// 이동" 두 곳에서 같은 인덱스를 재사용합니다(02번 문서 8번 탭 카드 13). 반환 타입은
/// <see cref="IFlowNodeIndex.FindNodesBySequenceId"/>와 공유하는 <see cref="NodeRef"/>입니다(v1.60 보강).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) DeviceMapPoller가 배치 폴링 대상 태그 목록을 얻고, 원시 값을 엔지니어링 값으로 변환
/// foreach (var tag in structure.GetTagsByMap("map-1"))
/// {
///     var raw = await structure.ReadRawAsync(tag.Id, ct);
///     object? engValue = structure.ApplyScale(tag, raw);
/// }
///
/// // 2) PlcTagWriteNode의 동시 쓰기 방지 — 같은 디바이스맵을 쓰는 노드끼리 락 공유
/// var gate = structure.GetWriteGate("map-1");
/// await gate.WaitAsync(ct);
/// try
/// {
///     var bytes = structure.ApplyInverseScale(structure.GetTag("tag-1"), 85.0);
///     await structure.WriteRawAsync("tag-1", bytes, ct);
/// }
/// finally { gate.Release(); }
///
/// // 3) 태그 삭제 전 참조 중인 노드가 있는지 확인(역방향 조회, NodeRef 재사용)
/// IReadOnlyList&lt;NodeRef&gt; blockers = structure.FindNodesByTagRef("tag-1");
/// bool canDelete = blockers.Count == 0;
/// </code>
/// </example>
public interface IStructureService
{
    /// <summary>태그 Id로 <see cref="TagRuntimeInfo"/>를 조회합니다.</summary>
    TagRuntimeInfo GetTag(string tagId);

    /// <summary>지정한 디바이스맵에 속한 모든 태그를 반환합니다. DeviceMapPoller가 배치 폴링 대상을 얻을 때 사용합니다.</summary>
    IEnumerable<TagRuntimeInfo> GetTagsByMap(string mapId);

    /// <summary>PLC(또는 시뮬레이터)에서 해당 태그의 원시 바이트를 읽습니다.</summary>
    Task<byte[]> ReadRawAsync(string tagId, CancellationToken ct);

    /// <summary>PLC(또는 시뮬레이터)에 원시 바이트를 씁니다. 쓰기 성공 여부를 반환합니다.</summary>
    Task<bool> WriteRawAsync(string tagId, byte[] raw, CancellationToken ct);

    /// <summary>원시 바이트를 <see cref="TagRuntimeInfo.Scale"/> 기준으로 엔지니어링 값으로 변환합니다. Scale이 없으면 원시 값을 그대로 반환합니다.</summary>
    object? ApplyScale(TagRuntimeInfo tag, byte[] raw);

    /// <summary><see cref="ApplyScale"/>의 역변환 — 엔지니어링 값을 PLC에 쓸 원시 바이트로 변환합니다.</summary>
    byte[] ApplyInverseScale(TagRuntimeInfo tag, object? engValue);

    /// <summary>같은 디바이스맵을 쓰는 쓰기 요청끼리 공유하는 락입니다(PlcTagWriteNode 동시 쓰기 방지).</summary>
    SemaphoreSlim GetWriteGate(string mapId);

    /// <summary>지정한 태그를 참조하는 모든 노드를 찾습니다. 태그 삭제 가능 여부 판단, 캔버스 역방향 하이라이트 이동에 사용됩니다.</summary>
    IReadOnlyList<NodeRef> FindNodesByTagRef(string tagId);
}
