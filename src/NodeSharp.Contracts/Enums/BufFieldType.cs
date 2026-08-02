namespace NodeSharp.Contracts.Enums;

// 한글명: 버퍼 필드 타입
/// <summary>
/// PLC 레지스터/버퍼에서 읽은 원시 바이트를 실제 값(정수/실수/문자열 등)으로 해석하는 방식을
/// 나타내는 필드 타입입니다. <see cref="Models.TagRuntimeInfo.BufType"/>이 이 값을 사용해
/// 바이트 배열을 엔지니어링 값으로 변환합니다.
/// 설계 근거: 02번 문서 8번 탭 카드 7. 값 목록은 <c>lssLib.Binary.BufType</c> 원본과 1:1
/// 대응하도록 정수(8/16/32/64비트 + 부호 없는 버전, BE/LE)·실수(Float/Double)·고정소수점
/// (Decimal)·논리(Bool/Bit)·문자열(Ascii/Utf8/Hex/Base64)·원시(Raw)·배열 7개 분류로 구성했습니다.
/// <c>LL-04a</c>에서 <c>lssLib.Binary</c>를 포팅할 때 <c>BufferParser</c>/<c>BufferWriter</c>의
/// <c>ReadXxx</c>/<c>WriteXxx</c> 메서드와 재작업 없이 그대로 매핑됩니다.
/// </summary>
/// <example>
/// <code>
/// // 토출압력 태그를 32비트 리틀엔디안 부동소수점(Modbus 표준 배치)으로 해석
/// var tag = new TagRuntimeInfo("tag-1", "토출압력", "map-1", 0, BufFieldType.FloatLE, null, null);
///
/// // lssLib.Binary.BufSchema 예(LL-04a 포팅 후 그대로 대응) — 여러 필드 타입을 한 프레임에 배치
/// // var schema = new BufSchema()
/// //     .Then("STX",    BufType.UInt8)      // 1바이트 헤더
/// //     .Then("Length", BufType.UInt16BE)   // 가변 길이 지시자(빅엔디안)
/// //     .Then("Value",  BufType.FloatBE)    // 4바이트 실수 값
/// //     .Then("Price",  BufType.DecimalLE)  // 16바이트 고정소수점 값
/// //     .Then("CRC",    BufType.UInt16BE);  // CRC16 검증
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

    /// <summary>부호 있는 32비트 정수(4바이트, 리틀엔디안).</summary>
    Int32LE,

    /// <summary>부호 있는 32비트 정수(4바이트, 빅엔디안).</summary>
    Int32BE,

    /// <summary>부호 있는 64비트 정수(8바이트, 리틀엔디안).</summary>
    Int64LE,

    /// <summary>부호 있는 64비트 정수(8바이트, 빅엔디안).</summary>
    Int64BE,

    // ── 정수(부호 없음) ──────────────────────────────────────────────
    /// <summary>부호 없는 8비트 정수(1바이트).</summary>
    UInt8,

    /// <summary>부호 없는 16비트 정수(2바이트, 리틀엔디안).</summary>
    UInt16LE,

    /// <summary>부호 없는 16비트 정수(2바이트, 빅엔디안).</summary>
    UInt16BE,

    /// <summary>부호 없는 32비트 정수(4바이트, 리틀엔디안).</summary>
    UInt32LE,

    /// <summary>부호 없는 32비트 정수(4바이트, 빅엔디안).</summary>
    UInt32BE,

    /// <summary>부호 없는 64비트 정수(8바이트, 리틀엔디안).</summary>
    UInt64LE,

    /// <summary>부호 없는 64비트 정수(8바이트, 빅엔디안).</summary>
    UInt64BE,

    // ── 실수 ────────────────────────────────────────────────────────
    /// <summary>32비트 부동소수점(4바이트, 리틀엔디안 — Modbus 표준 배치).</summary>
    FloatLE,

    /// <summary>32비트 부동소수점(4바이트, 빅엔디안).</summary>
    FloatBE,

    /// <summary>64비트 부동소수점(8바이트, 리틀엔디안).</summary>
    DoubleLE,

    /// <summary>64비트 부동소수점(8바이트, 빅엔디안).</summary>
    DoubleBE,

    // ── 고정소수점 ──────────────────────────────────────────────────
    /// <summary>16바이트 고정소수점(decimal, 리틀엔디안) — 손실 없는 정밀도가 필요한 값.</summary>
    DecimalLE,

    /// <summary>16바이트 고정소수점(decimal, 빅엔디안).</summary>
    DecimalBE,

    // ── 논리 ────────────────────────────────────────────────────────
    /// <summary>불리언 값(1바이트).</summary>
    Bool,

    /// <summary>비트 단위 플래그(1바이트 안의 특정 비트).</summary>
    Bit,

    // ── 문자열 ──────────────────────────────────────────────────────
    /// <summary>ASCII 문자열(필드 크기만큼의 바이트).</summary>
    StringAscii,

    /// <summary>UTF-8 문자열(필드 크기만큼의 바이트).</summary>
    StringUtf8,

    /// <summary>16진수 문자열 표현(디버그·로그 표시용).</summary>
    Hex,

    /// <summary>Base64 문자열 표현.</summary>
    Base64,

    // ── 원시 ────────────────────────────────────────────────────────
    /// <summary>해석하지 않은 원시 바이트 그대로.</summary>
    Raw,

    // ── 배열(고정 개수 반복) ─────────────────────────────────────────
    /// <summary>Int16LE 배열.</summary>
    Int16Array,

    /// <summary>UInt16LE 배열.</summary>
    UInt16Array,

    /// <summary>Int32LE 배열.</summary>
    Int32Array,

    /// <summary>UInt32LE 배열.</summary>
    UInt32Array,

    /// <summary>Int64LE 배열.</summary>
    Int64Array,

    /// <summary>UInt64LE 배열.</summary>
    UInt64Array,

    /// <summary>FloatLE 배열.</summary>
    FloatArray,

    /// <summary>DoubleLE 배열.</summary>
    DoubleArray,

    /// <summary>DecimalLE 배열(가격 배열 등 금융 프레임에 사용).</summary>
    DecimalLEArray,

    /// <summary>DecimalBE 배열.</summary>
    DecimalBEArray
}
