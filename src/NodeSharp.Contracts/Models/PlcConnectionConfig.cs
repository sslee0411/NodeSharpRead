namespace NodeSharp.Contracts.Models;

// 한글명: PLC 접속 설정
/// <summary>
/// <see cref="Interfaces.IProtocolDriver.ConnectAsync"/>에 전달하는 접속 정보입니다. TCP(<see cref="Host"/>/
/// <see cref="Port"/>)와 Serial/RTU(<see cref="ComPort"/>/<see cref="BaudRate"/>) 두 모드를 함께 담으며,
/// 드라이버가 <c>IsRtu</c> 여부에 따라 필요한 쪽만 사용합니다.
/// 설계 근거: 02번 문서 11번 탭 카드 8. <c>ConnectAsync(PlcConnectionConfig config, ...)</c>는 원본
/// 코드 조각에 파라미터 타입으로만 등장하고 정식 필드 선언이 없던 공백이라(v1.67 발견한 것과 동일
/// 유형), 8번 탭 <c>PlcNode</c>의 <c>Host</c>/<c>Port</c> 속성과 <c>ModbusDriver.IsRtu</c>가 암시하는
/// Serial 접속 정보(ComPort/BaudRate)를 근거로 이 Step에서 최소 형태로 신규 정의했습니다.
/// </summary>
/// <param name="Host">Modbus TCP 접속 대상 IP/호스트명. RTU 모드에서는 사용하지 않습니다.</param>
/// <param name="Port">Modbus TCP 포트(기본 502). RTU 모드에서는 사용하지 않습니다.</param>
/// <param name="ComPort">Modbus RTU 접속 시 사용할 시리얼 포트 이름(예: <c>"COM3"</c>). TCP 모드에서는 <c>null</c>.</param>
/// <param name="BaudRate">Modbus RTU 시리얼 통신 속도(기본 9600bps). TCP 모드에서는 의미 없음.</param>
/// <example>
/// <code>
/// // Modbus TCP 접속(PlcNode.CommType="ModbusTcp", 8번 탭)
/// var tcpConfig = new PlcConnectionConfig(Host: "192.168.1.10", Port: 502);
/// await driver.ConnectAsync(tcpConfig, ct);
///
/// // Modbus RTU(Serial) 접속 — ModbusDriver.IsRtu가 true일 때 ComPort/BaudRate만 사용
/// var rtuConfig = new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM3", BaudRate: 19200);
/// await driver.ConnectAsync(rtuConfig, ct);
/// </code>
/// </example>
public sealed record PlcConnectionConfig(string Host, int Port, string? ComPort = null, int BaudRate = 9600);
