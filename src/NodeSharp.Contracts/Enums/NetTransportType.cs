namespace NodeSharp.Contracts.Enums;

/// <summary>
/// D:\lssLib.Net이 지원하는 9종 raw 전송(Transport) 종류입니다. 여기서 "raw"란 바이트를
/// 주고받는 하위 계층만 담당한다는 뜻으로, Modbus·MQTT 같은 상위 프로토콜의 규약(주소 체계,
/// 함수 코드, QoS 등)은 다루지 않습니다(그 역할은 <c>IProtocolDriver</c>가 이 Transport 위에서 담당).
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 11번 탭(lssLib 노드 모듈화) 카드 1. lssLib.Net 원본은
/// ProjectReference로 직접 참조하지 않고 <b>포팅(복사)</b>해서 <c>NodeSharp.Util.Net</c>에
/// 반입할 예정이지만(LL-05a Step), 이 Enum 자체는 Registry·PropertySchema·PLC 통신 설정 등
/// Contracts를 참조하는 여러 프로젝트에서 훨씬 이전 단계(예: 8번 탭 PlcNode 통신 설정 UI,
/// PD-01a/b ModbusDriver)부터 값으로만 참조되므로, lssLib 실제 포팅(Phase 12)을 기다리지 않고
/// <b>Contracts에 먼저 정의</b>합니다. LL-05a에서 lssLib.Net을 포팅할 때는 이 Enum을 재정의하지
/// 않고 그대로 재사용합니다.
/// </para>
/// <para>
/// 각 값의 용도:
/// </para>
/// </remarks>
/// <example>
/// PLC 통신 프로토콜 드라이버(11번 탭 카드 8, PD-01a/b에서 구현)가 내부적으로 raw Transport를
/// 선택하는 예:
/// <code>
/// // Modbus TCP는 Tcp Transport, Modbus RTU(RS-485/RS-232)는 Serial Transport 위에서 동작
/// var transportType = isRtu ? NetTransportType.Serial : NetTransportType.Tcp;
///
/// // 실제 PLC 하드웨어 없이 개발/테스트할 때는 시뮬레이터용 Virtual Transport 사용(PD-01c)
/// var simTransportType = NetTransportType.Virtual;
/// </code>
/// </example>
public enum NetTransportType
{
    /// <summary>TCP 소켓. Modbus TCP, 범용 TCP 서버/클라이언트 노드 등에서 사용합니다.</summary>
    Tcp,

    /// <summary>UDP 소켓. 연결 없이 빠르게 보내는 브로드캐스트/저지연 용도에 사용합니다.</summary>
    Udp,

    /// <summary>RS-232/RS-485 시리얼 포트. Modbus RTU 등 산업 현장의 유선 시리얼 통신에 사용합니다.</summary>
    Serial,

    /// <summary>HTTP(S) 클라이언트/서버. HttpRequestNode, HTTP in/response 노드 등에서 사용합니다.</summary>
    Http,

    /// <summary>WebSocket. 실시간 양방향 스트리밍이 필요한 WebSocket in/out 노드에서 사용합니다.</summary>
    WebSocket,

    /// <summary>MQTT 브로커 연결. 상위의 <see cref="MqttQos"/>·Topic·Retain·LWT 시맨틱은 MqttInNode/MqttOutNode가 별도로 처리합니다.</summary>
    Mqtt,

    /// <summary>Windows Named Pipe. Editor↔Runner 같은 동일 PC 내 프로세스 간 통신에 사용합니다.</summary>
    NamedPipe,

    /// <summary>공유 메모리(Memory-Mapped File 등). 매우 높은 처리량이 필요한 동일 PC 내 통신에 사용합니다.</summary>
    SharedMemory,

    /// <summary>
    /// 가상(메모리 기반) 전송 — 실제 네트워크/하드웨어를 쓰지 않는 시뮬레이터용입니다.
    /// 실제 PLC 없이 Modbus 플로우를 개발·테스트할 때 <c>VirtualModbusSlave</c>(PD-01c)가 이 값을 사용합니다.
    /// </summary>
    Virtual
}
