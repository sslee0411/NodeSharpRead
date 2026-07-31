namespace NodeSharp.Contracts.Models;

/// <summary>
/// 프로토콜 드라이버 플러그인이 자신을 설명하는 매니페스트입니다. <c>PluginManifest</c>(CT-06b, 노드
/// 플러그인용)와 동일한 목적을 <c>IProtocolDriver</c> 구현체(LS산전 XGT, 미쯔비시 A/QnA, CIMON HD 등)에
/// 대해 수행합니다 — 드라이버 플러그인도 NodeSharp.Contracts만 참조하므로 이 레코드를 직접 생성할 수
/// 있어야 합니다.
/// 설계 근거: 02번 문서 11번 탭 카드 8(★ v1.71 정정 — Contracts 재컴파일 없이 새 PLC 프로토콜을 추가할
/// 수 있도록 동적 등록 구조 도입).
/// </summary>
/// <param name="ProtocolTypeName"><c>Enums.ProtocolDriverType</c>의 상수 중 하나이거나, 새 프로토콜이면
/// 플러그인 작성자가 <c>"제조사.모델"</c> 관례로 정한 새 문자열(예: <c>"LS.XGT"</c>).</param>
/// <param name="DriverVersion">드라이버 플러그인 자체의 버전(참고용 — 호환성 판단에는 쓰이지 않음).</param>
/// <param name="RequiredContractsVersion">이 드라이버가 요구하는 <c>NodeSharp.Contracts</c> 버전(SemVer
/// 형식, 예: <c>"1.0.0"</c>). <c>ProtocolDriverRegistry</c>가 등록 시 현재 Contracts 버전과 비교합니다.</param>
/// <example>
/// <code>
/// // LS산전 XGT 드라이버 플러그인이 자신의 dll에 포함하는 매니페스트
/// var manifest = new ProtocolDriverManifest(
///     ProtocolTypeName: NodeSharp.Contracts.Enums.ProtocolDriverType.LsXgt,
///     DriverVersion: "1.0.0",
///     RequiredContractsVersion: "1.0.0");
///
/// // ProtocolDriverRegistry가 등록 전 SemVer 호환성 체크(주 버전이 다르면 등록 거부)
/// bool ok = registry.TryRegister(manifest, typeof(LsXgtDriver));
/// </code>
/// </example>
public sealed record ProtocolDriverManifest(string ProtocolTypeName, string DriverVersion, string RequiredContractsVersion);
