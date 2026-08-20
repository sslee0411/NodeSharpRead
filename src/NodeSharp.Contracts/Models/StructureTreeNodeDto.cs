namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 구조 설정 트리 노드 저장용 DTO
/// 역활 및 기능 : Editor 전용 <c>NodeSharp.Editor.Structure.StructureTreeNode</c>(장비/PLC/디바이스맵/
/// 태그/스케일/알람 6종 구체 클래스)를 device.json에 저장하거나 그로부터 복원하기 위한, WPF에
/// 의존하지 않는 순수 데이터 표현입니다.
///
/// (ED-D03) 02번 설계문서 8번 탭 카드6 "저장 포맷 및 Runner 연동"이 정의하는
/// <c>DeviceTreeDto(IReadOnlyList&lt;StructureTreeNodeDto&gt; Devices)</c>의 자식 노드 타입입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>6종 클래스를 1종 DTO로 평탄화한 이유</b>: <c>StructureTreeNode</c>의 6개 구체 클래스는
/// 서로 다른 프로퍼티(<c>PlcNode.Host</c>, <c>ScaleNode.RawMin</c> 등)를 갖지만, <see cref="NodeType"/>
/// (예: "Device"/"Plc"/"DeviceMap"/"Tag"/"Scale"/"Alarm") + <see cref="Properties"/>(그 타입의
/// <c>PropertySchema.Key</c> → 문자열 값 딕셔너리) 조합만으로 6종 전부를 표현할 수 있습니다.
/// <c>StructureNodePropertyDialog</c>(ED-D02a/b)가 이미 "<c>PropertyField.Key</c>는 항상 C# 프로퍼티
/// 이름과 일치한다"는 규칙으로 리플렉션 기반 범용 편집기 1개를 만든 것과 동일한 이유로, 여기서도
/// System.Text.Json의 다형성 직렬화(<c>JsonDerivedType</c> 등, 타입마다 별도 설정 필요) 대신 이
/// 평탄화 방식을 택해 6종을 위한 별도 DTO/직렬화 설정을 유지보수하지 않아도 되게 했습니다 —
/// <c>NodeSharp.Editor.Structure.StructureTreeMapper</c>가 실제 변환(왕복)을 담당합니다.</item>
/// <item><b>Id 보존</b>: 저장 전 원래 <c>StructureTreeNode.Id</c>(<c>init</c>) 값을 그대로 담아,
/// 다시 불러왔을 때도 같은 Id를 유지합니다 — ED-D04(TagRef 연동)에서 캔버스 노드가 이 Id로 태그를
/// 참조할 예정이므로, 저장/로드를 거칠 때마다 Id가 바뀌면 참조가 끊어집니다.</item>
/// </list>
/// </remarks>
/// <param name="Id">원본 <c>StructureTreeNode.Id</c> — TagRef 등 참조의 기준(왕복 시 보존).</param>
/// <param name="NodeType">6종 구체 클래스 구분자 — "Device"/"Plc"/"DeviceMap"/"Tag"/"Scale"/"Alarm" 중 하나.</param>
/// <param name="Name">트리에 표시되는 이름.</param>
/// <param name="Description">사용자가 남긴 설명(선택 사항, 빈 문자열 가능).</param>
/// <param name="Properties">그 타입의 <c>PropertySchema</c> 각 <c>Key</c>에 대응하는 값을 문자열로
/// 담은 딕셔너리 — null이면 그 프로퍼티는 값이 없었다는 뜻(예: <c>AlarmNode</c>의 비워둔 <c>double?</c> 필드).</param>
/// <param name="Children">이 노드의 자식 목록(재귀) — 잎 노드(스케일/알람)는 빈 목록.</param>
public sealed record StructureTreeNodeDto(
    string Id,
    string NodeType,
    string Name,
    string Description,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyList<StructureTreeNodeDto> Children);
