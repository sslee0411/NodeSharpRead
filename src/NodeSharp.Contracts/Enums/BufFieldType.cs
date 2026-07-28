namespace NodeSharp.Contracts.Enums;

/// <summary>
/// PLC 레지스터/버퍼에서 태그 하나를 읽어올 때 그 바이트를 어떻게 해석할지 나타내는 필드 타입입니다.
/// <see cref="Models.TagRuntimeInfo.BufType"/>이 이 값을 사용해 원시 바이트(<c>byte[]</c>)를
/// 실제 엔지니어링 값(정수/실수/문자열 등)으로 변환합니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 8번 탭 카드 7 — <c>TagRuntimeInfo.BufType</c>이 이 타입을 참조하지만,
/// 정식 선언이 문서 어디에도 없던 공백이었습니다. <c>CT-01b</c>의 <c>NetTransportType</c>/
/// <c>PropertyFieldType</c>과 동일한 원칙 — <c>lssLib.Binary</c> 전체 포팅(<c>LL-04a</c>, Phase 11)을
/// 기다리지 않고, 지금 필요한 값을 <c>Contracts</c>에 먼저 확정하고 <c>LL-04a</c>는 이 Enum을
/// 재정의하지 않고 재사용합니다.
/// </para>
/// <para>
/// <b>값 목록의 근거(v1.42, 강화)</b>: 최초 버전(v1.41)은 <c>iiot-system-arch</c> 스킬의 BufType
/// 참조표(예시 6개 값)만 반영했었으나, 이 프로젝트의 개발 베이스로 지정된
/// <c>https://github.com/sslee0411/lssLib.git</c>의 실제 <c>lssLib.Binary.BufType</c> 전체
/// 목록(<c>dev-csharp</c> 스킬의 "BufType — 지원 타입 전체 목록" 표)을 근거로 다시 작성했습니다.
/// <c>LL-04a</c>에서 <c>lssLib.Binary.BufSchema</c>/<c>BufferParser</c>/<c>BufferWriter</c>를 그대로
/// 포팅할 때 이 Enum의 값이 원본과 1:1로 대응해야 <c>ReadXxx</c>/<c>WriteXxx</c> 메서드 매핑에
/// 재작업이 없습니다 — 지금 원본 목록 전체를 옮겨 적는 것이 오히려 "과설계 방지" 원칙에 부합합니다
/// (나중에 값이 부족해 다시 문서·코드를 고치는 재작업을 피함).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 8번 탭 예: 40001번지(Modbus 4xxxx 주소)부터 시작하는 토출압력 태그를 FloatLE로 해석
/// var tag = new TagRuntimeInfo(
///     Id: "tag-1", Name: "토출압력", ParentMapId: "map-1",
///     Offset: 0, BufType: BufFieldType.FloatLE,
///     Scale: null, Alarm: null);
///
/// // lssLib.Binary.BufSchema 예 (LL-04a 포팅 후 그대로 대응)
/// // var schema = new BufSchema()
/// //     .Then("STX",    BufType.UInt8)
/// //     .Then("Length", BufType.UInt16BE)
/// //     .Then("Value",  BufType.FloatBE)
/// //     .Then("Price",  BufType.DecimalLE);
/// </code>
/// </example>
public enum BufFieldType
{
    // ── 정수(부호 있음) — 1바이트는 엔디안 구분 없음 ──────────────────────
    /// <summary>부호 있는 8비트 정수(1바이트).</summary>
    Int8,

    /// <summary>부호 있는 16비트 정수(2바이트, 리틀엔디안).</summary>
    Int16LE,

    /// <summary>부호 있는 16비트 정수(2바이트, 빅엔디안).</summary>
    Int16BE,

    /// <summary>부호 있는 32비트 정수(4바이트/2레지스터, 리틀엔디안 — Modbus에서 흔히 쓰는 표준 배치).</summary>
    Int32LE,

    /// <summary>부호 있는 32비트 정수(4바이트/2레지스터, 빅엔디안).</summary>
    Int32BE,

    /// <summary>부호 있는 64비트 정수(8바이트, 리틀엔디안).</summary>
    Int64LE,

    /// <summary>부호 있는 64비트 정수(8바이트, 빅엔디안).</summary>
    Int64BE,

    // ── 정수(부호 없음) ──────────────────────────────────────────────
    /// <summary>부호 없는 8비트 정수(1바이트).</summary>
    UInt8,

    /// <summary>부호 없는 16비트 정수(2바이트, 리틀엔디안) — 상태값 등에 흔히 사용.</summary>
    UInt16LE,

    /// <summary>부호 없는 16비트 정수(2바이트, 빅엔디안) — 가변 길이 지시자·CRC 등 프로토콜 헤더 필드에 사용.</summary>
    UInt16BE,

    /// <summary>부호 없는 32비트 정수(4바이트/2레지스터, 리틀엔디안).</summary>
    UInt32LE,

    /// <summary>부호 없는 32비트 정수(4바이트/2레지스터, 빅엔디안).</summary>
    UInt32BE,

    /// <summary>부호 없는 64비트 정수(8바이트, 리틀엔디안).</summary>
    UInt64LE,

    /// <summary>부호 없는 64비트 정수(8바이트, 빅엔디안).</summary>
    UInt64BE,

    // ── 실수 ────────────────────────────────────────────────────────
    /// <summary>32비트 부동소수점(4바이트/2레지스터, 리틀엔디안 — Modbus 표준 배치).</summary>
    FloatLE,

    /// <summary>32비트 부동소수점(4바이트/2레지스터, 빅엔디안).</summary>
    FloatBE,

    /// <summary>64비트 부동소수점(8바이트, 리틀엔디안).</summary>
    DoubleLE,

    /// <summary>64비트 부동소수점(8바이트, 빅엔디안).</summary>
    DoubleBE,

    // ── 고정소수점 ──────────────────────────────────────────────────
    /// <summary>.NET <see cref="decimal"/> 고정소수점(16바이트, 리틀엔디안) — 금융/정밀 계량 값 등 손실 없는 정밀도가 필요한 필드.</summary>
    DecimalLE,

    /// <summary>.NET <see cref="decimal"/> 고정소수점(16바이트, 빅엔디안).</summary>
    DecimalBE,

    // ── 논리 ────────────────────────────────────────────────────────
    /// <summary>불리언 값(1바이트 전체를 0/0이 아님으로 판정).</summary>
    Bool,

    /// <summary>비트 단위 플래그(1바이트 안의 특정 비트 하나) — 상태 레지스터의 비트 마스크 필드에 사용.</summary>
    Bit,

    // ── 문자열 ──────────────────────────────────────────────────────
    /// <summary>고정/가변 길이 ASCII 문자열(필드 크기만큼의 바이트).</summary>
    StringAscii,

    /// <summary>고정/가변 길이 UTF-8 문자열(필드 크기만큼의 바이트).</summary>
    StringUtf8,

    /// <summary>바이트를 16진수 문자열로 표현(필드 크기만큼의 바이트, 디버그·로그 표시용).</summary>
    Hex,

    /// <summary>바이트를 Base64 문자열로 표현(필드 크기만큼의 바이트).</summary>
    Base64,

    // ── 원시 ────────────────────────────────────────────────────────
    /// <summary>해석하지 않고 원시 바이트 그대로 보관(필드 크기만큼의 바이트) — 페이로드 통과·추후 수동 파싱용.</summary>
    Raw,

    // ── 배열(고정 개수 반복) ─────────────────────────────────────────
    /// <summary><see cref="BufFieldType.Int16LE"/> 원소의 배열(원소 개수 × 2바이트).</summary>
    Int16Array,

    /// <summary><see cref="BufFieldType.UInt16LE"/> 원소의 배열(원소 개수 × 2바이트).</summary>
    UInt16Array,

    /// <summary><see cref="BufFieldType.Int32LE"/> 원소의 배열(원소 개수 × 4바이트).</summary>
    Int32Array,

    /// <summary><see cref="BufFieldType.UInt32LE"/> 원소의 배열(원소 개수 × 4바이트).</summary>
    UInt32Array,

    /// <summary><see cref="BufFieldType.Int64LE"/> 원소의 배열(원소 개수 × 8바이트).</summary>
    Int64Array,

    /// <summary><see cref="BufFieldType.UInt64LE"/> 원소의 배열(원소 개수 × 8바이트).</summary>
    UInt64Array,

    /// <summary><see cref="BufFieldType.FloatLE"/> 원소의 배열(원소 개수 × 4바이트).</summary>
    FloatArray,

    /// <summary><see cref="BufFieldType.DoubleLE"/> 원소의 배열(원소 개수 × 8바이트).</summary>
    DoubleArray,

    /// <summary><see cref="BufFieldType.DecimalLE"/> 원소의 배열(원소 개수 × 16바이트) — 금융 프레임(가격 배열 등)에 사용.</summary>
    DecimalLEArray,

    /// <summary><see cref="BufFieldType.DecimalBE"/> 원소의 배열(원소 개수 × 16바이트).</summary>
    DecimalBEArray
}
