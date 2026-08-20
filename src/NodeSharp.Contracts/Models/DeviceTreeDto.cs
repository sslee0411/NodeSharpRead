namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 구조 설정 트리 저장 파일 최상위 DTO
/// 역활 및 기능 : device.json 파일 전체(장비 트리 루트 목록)를 표현하는 최상위 레코드
///
/// (ED-D03) 02번 설계문서 8번 탭 카드6 "저장 포맷 및 Runner 연동"이 정의하는
/// <c>DeviceTreeDto(IReadOnlyList&lt;StructureTreeNodeDto&gt; Devices)</c>를 그대로 포팅했습니다.
/// <c>NodeSharp.Editor.Core.Config.DeviceStore</c>가 <c>JsonWriteService.WriteAtomicAsync</c>(EC-04에서
/// flows.json에 쓴 것과 동일한 .tmp→<c>File.Replace</c> 원자적 저장 + .signal 발행 패턴)로 이 레코드를
/// device.json에 저장합니다.
/// </summary>
/// <param name="Devices">6단계 트리의 루트(1단계, <c>DeviceNode</c>) 목록 — 각 항목이 <see cref="StructureTreeNodeDto"/>로,
/// 그 안에 PLC→디바이스맵→태그→스케일/알람까지 재귀적으로 포함됩니다.</param>
public sealed record DeviceTreeDto(IReadOnlyList<StructureTreeNodeDto> Devices);
