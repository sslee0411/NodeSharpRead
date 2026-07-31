namespace NodeSharp.Contracts.Enums;

/// <summary>
/// <c>IProtocolDriver.Type</c>에 사용하는 프로토콜 식별 문자열의 잘 알려진 값 모음입니다.
/// (★ v1.71 정정) 애초 이 타입은 <c>enum ProtocolDriverType { Modbus }</c>(고정 집합, CT-09 최초 구현)
/// 였으나, "LS산전 XGT·미쯔비시 A/QnA·CIMON HD 등 새 PLC 프로토콜을 Contracts 재컴파일 없이 추가할 수
/// 있어야 한다"는 요구에 따라 <c>string</c> 기반 식별자로 전환했습니다 — <c>INodeTypeDescriptor.TypeName</c>
/// (9번 탭 카드1)이 노드 종류를 문자열로 여는 것과 동일한 설계 원칙입니다. Enum이 아니므로 여기 없는
/// 값도 <c>ProtocolDriverRegistry</c>(Registry 소속)로 얼마든지 새로 등록할 수 있습니다.
/// 설계 근거: 02번 문서 11번 탭 카드 8.
/// </summary>
/// <remarks>
/// 아래 상수는 지금까지 설계 문서에 등장한 "잘 알려진" 값의 이름 충돌 방지용 카탈로그일 뿐입니다.
/// 명명 규칙: <c>"제조사.모델"</c>(마침표 구분). 실제 구현체가 없어도 상수만 먼저 예약할 수 있습니다
/// (Modbus.Tcp/Rtu만 PD-01a/b로 구현 확정, 나머지는 향후 플러그인 프로젝트에서 구현 예정 — 10번 탭
/// 카드14 로드맵).
/// </remarks>
/// <example>
/// <code>
/// // 알려진 값은 상수로 안전하게 참조
/// string type = ProtocolDriverType.ModbusTcp;
///
/// // 카탈로그에 없는 새 프로토콜도 문자열이면 그대로 등록 가능(플러그인 쪽에서 자체 상수 정의)
/// var manifest = new ProtocolDriverManifest("Vendor.NewProtocol", "1.0.0", "1.0.0");
/// registry.TryRegister(manifest, typeof(NewProtocolDriver));
/// </code>
/// </example>
public static class ProtocolDriverType
{
    /// <summary>Modbus TCP(MBAP 헤더). 1차 구현체는 <c>ModbusDriver(IsRtu: false)</c>(<c>PD-01a</c>).</summary>
    public const string ModbusTcp = "Modbus.Tcp";

    /// <summary>Modbus RTU(Serial, CRC16 포함). 1차 구현체는 <c>ModbusDriver(IsRtu: true)</c>(<c>PD-01b</c>).</summary>
    public const string ModbusRtu = "Modbus.Rtu";

    /// <summary>Siemens S7(S7comm). 10번 탭 카드14 로드맵(RM-05) — S7netplus 등 외부 라이브러리 선정 후 구현 예정.</summary>
    public const string SiemensS7 = "Siemens.S7";

    /// <summary>LS산전(구 LG산전) XGT 시리즈 전용 프로토콜. 구현 예정(플러그인, <c>nodes/NodeSharp.Nodes.LsXgt</c>).</summary>
    public const string LsXgt = "LS.XGT";

    /// <summary>미쯔비시전기 MELSEC A 프레임(A컴퓨터링크) 프로토콜. 구현 예정(플러그인).</summary>
    public const string MitsubishiA = "Mitsubishi.A";

    /// <summary>미쯔비시전기 MELSEC QnA 프레임 프로토콜. 구현 예정(플러그인).</summary>
    public const string MitsubishiQnA = "Mitsubishi.QnA";

    /// <summary>CIMON(신성이엔지) HD 프로토콜. 구현 예정(플러그인).</summary>
    public const string CimonHd = "CIMON.HD";
}
