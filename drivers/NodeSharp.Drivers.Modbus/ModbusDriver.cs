using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Drivers.Modbus;

/// <summary>
/// Class명 : Modbus 프로토콜 드라이버
/// 역활 및 기능 : Modbus TCP(MBAP 헤더)·RTU(Serial+CRC16) 위에서 Holding Register 읽기/쓰기를 수행하는 <see cref="IProtocolDriver"/> 구현체
///
/// (PD-01a) 02번 설계문서 11번 탭 카드8이 예시로 든 <c>new ModbusDriver(IsRtu: false)</c> 그대로,
/// TCP 모드(<see cref="ProtocolDriverType.ModbusTcp"/>)를 구현합니다. (PD-01b, ★ 추가) Serial 기반
/// RTU 모드(<see cref="ProtocolDriverType.ModbusRtu"/>, <see cref="System.IO.Ports.SerialPort"/> +
/// CRC16)도 이 클래스에 이어서 구현했습니다 — <see cref="ConnectAsync"/>가 <c>isRtu</c> 여부에 따라
/// TCP/RTU 두 경로 중 하나로 연결합니다. (PD-01c, ★ 추가) <see cref="VirtualSlave"/>를 지정하면 실제
/// TCP/RTU 대신 <see cref="VirtualModbusSlave"/>(메모리 기반 가상 슬레이브, <c>NetTransportType.Virtual</c>)에
/// 인메모리로 연결합니다 — 실제 하드웨어 없이 개발·테스트할 때 사용합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>NetTransportType.Tcp/Serial 대신 System.Net.Sockets.TcpClient·System.IO.Ports.SerialPort를
/// 직접 사용</b>: 03번 Step맵의 PD-01a/PD-01b 설명은 각각 "LL-05a로 포팅된 NodeSharp.Util.Net의
/// NetTransportType.Tcp/Serial 위에서"라고 적혀 있지만, LL-05a(lssLib.Net 9종 Transport 포팅, Phase 12)는
/// 이 시점(Phase 9)에 아직 ⏳ 대기 상태로 실제 코드가 전혀 없습니다(<c>NodeSharp.Util.Net</c> 네임스페이스·
/// <c>INetTransport</c> 어디에도 존재하지 않음, 저장소 전체 검색으로 확인). ED-D04(TagRef 연동)가 이
/// 드라이버의 존재를 전제한다는 설계 순서 메모(PD-01a 행 desc, "표 위치≠실행 순서")를 지키기 위해,
/// LL-05a를 먼저 통째로 구현하는 대신 이 클래스 내부에서 최소한의 소켓/시리얼 처리(MBAP 프레이밍·
/// RTU ADU+CRC16)만 직접 수행합니다 — LL-05a가 실제로 구현되면 그 추상화를 쓰도록 리팩터링할 예정이며,
/// 그때까지는 이 방식이 임시 구현임을 여기 남겨둡니다.</item>
/// <item><b>주소 규약</b>: <see cref="ReadAsync"/>/<see cref="WriteAsync"/>의 <c>address</c>는
/// <see cref="IProtocolDriver"/> 인터페이스 문서의 예시(<c>ReadAsync(startAddress: 40001, ...)</c>)를
/// 그대로 따라 <b>Modbus 프로토콜 주소 필드에 그대로 실리는 값</b>입니다 — 전통적인 "40001 = 0번지"
/// 오프셋 변환은 하지 않습니다(그런 변환 규칙이 설계 문서 어디에도 정의돼 있지 않음). 호출 측
/// (<c>DeviceMapNode.StartAddress</c>, 8번 탭)이 실제 프로토콜 주소를 그대로 넘겨야 합니다. TCP/RTU
/// 두 모드 모두 동일한 규약을 씁니다(PDU 구성은 두 모드가 공통, 달라지는 것은 겉봉투뿐 — 아래
/// "TCP/RTU 겉봉투 차이" 항목 참고).</item>
/// <item><b>레지스터 단위</b>: Modbus Holding Register는 2바이트 고정폭이라 <c>lengthBytes</c>(읽기)와
/// <c>data.Length</c>(쓰기)는 반드시 2의 배수여야 합니다 — 아니면 <see cref="ArgumentException"/>.</item>
/// <item><b>쓰기 함수 코드 선택</b>: <c>data.Length == 2</c>(레지스터 1개)면 FC06(Write Single
/// Register), 그 이상이면 FC16/0x10(Write Multiple Registers)을 사용합니다 — 03번 Step맵 PD-01a
/// 설명의 "0x06·0x10 Write" 둘 다를 구현했습니다.</item>
/// <item><b>예외 응답</b>: 슬레이브가 Modbus 예외 응답(함수 코드 최상위 비트 설정, 1바이트 예외
/// 코드)을 보내면 <see cref="ModbusException"/>을 던집니다.</item>
/// <item><b>(PD-01b, ★ 추가) TCP/RTU 겉봉투 차이</b>: PDU(함수 코드+주소/데이터)를 조립하는 로직은
/// TCP/RTU가 공통이고(<see cref="ReadAsync"/>/<see cref="WriteAsync"/>가 만드는 <c>pdu</c>), 그 PDU를
/// 실제로 주고받는 겉봉투만 모드별로 다릅니다 — TCP는 MBAP 헤더(6바이트: TransactionId·ProtocolId=0·
/// Length)를 앞에 붙이고(<see cref="SendAndReceiveTcpAsync"/>), RTU는 슬레이브 주소(1바이트) 뒤에
/// PDU를 붙이고 CRC16(2바이트, Low-High 순)을 계산해 끝에 덧붙입니다(<see cref="SendAndReceiveRtuAsync"/>).
/// RTU 응답은 CRC를 재계산해 수신한 CRC와 비교하고, 일치하지 않으면 <see cref="ModbusException"/>을 던져
/// 거부합니다(오류 응답이 <see cref="Cache"/>류에 반영되지 않도록, 호출자인 <c>ReadAsync</c>/<c>WriteAsync</c>가
/// 예외를 그대로 전파하므로 손상된 값은 절대 정상 반환값으로 섞이지 않습니다).</item>
/// <item><b>(PD-01b, ★ 추가) RTU 시리얼 설정 고정값</b>: <see cref="PlcConnectionConfig"/>가
/// ComPort/BaudRate만 정의하므로("최소 형태로 신규 정의", PlcConnectionConfig.cs 문서 참고), 나머지
/// 시리얼 파라미터는 산업 현장 Modbus RTU 관례값으로 고정합니다 — Parity=None, DataBits=8,
/// StopBits=One(8N1), Read/WriteTimeout=3000ms. 장비별로 이 값을 달리해야 하는 사례가 나오면
/// <see cref="PlcConnectionConfig"/>에 필드를 추가하는 후속 Step에서 다룹니다(사용자 확인 없이 결정,
/// 근거는 이 remarks에 기록).</item>
/// <item><b>(PD-01b, ★ 추가) RT-10 SharedResourceManager 연동</b>: 이 클래스는 <see cref="ISharedServiceNode"/>도
/// 구현해 <c>SharedResourceManager.AcquireAsync</c>로 공유 관리될 수 있습니다(<see cref="Id"/>·
/// <see cref="Config"/>·<see cref="StartAsync"/>·<see cref="StopAsync"/> 참고) — 같은 PLC(같은 Id)를
/// 참조하는 TagNode 여러 개가 배포돼도 실제 <c>ModbusDriver</c> 인스턴스와 연결은 1개만 생성됩니다
/// (LL-05b <c>NetIoNode</c>가 TcpListener 등을 공유하는 것과 동일한 참조 카운트 원칙, RT-10 문서 참고).
/// <see cref="Config"/>를 지정하지 않고 기존처럼 <see cref="ConnectAsync"/>를 직접 호출하는 사용법
/// (PD-01a 테스트 등)은 이 확장과 무관하게 그대로 동작합니다 — SharedResourceManager를 거칠 때만
/// <see cref="StartAsync"/>가 필요합니다.</item>
/// <item><b>(PD-01c, ★ 추가) VirtualSlave — 가상 Modbus 슬레이브 연결</b>: <see cref="VirtualSlave"/>에
/// <see cref="VirtualModbusSlave"/> 인스턴스를 지정하면 <see cref="ConnectAsync"/>가 실제
/// <see cref="TcpClient"/>/<see cref="SerialPort"/> 대신 그 슬레이브가 만든 인메모리 <see cref="Stream"/>에
/// 연결합니다 — 이후 <see cref="ReadAsync"/>/<see cref="WriteAsync"/>/PDU 처리는 TCP 경로와 완전히
/// 동일하게 동작합니다(MBAP 프레이밍 공용, <see cref="VirtualModbusSlave"/> 클래스 문서 참고). RTU
/// (<c>isRtu: true</c>)와 함께 쓰면 <see cref="ArgumentException"/>을 던집니다 — <see cref="VirtualModbusSlave"/>는
/// MBAP만 지원하기 때문입니다(범위: 사용자 확인 "핵심만 우선 구현", 2026-09-01 — Editor 시뮬레이터
/// 패널·PlcNode 체크박스·캔버스 반영은 PD-01d로 분리).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) TCP 모드 — 직접 연결(SharedResourceManager 없이)
/// IProtocolDriver driver = new ModbusDriver();
/// await driver.ConnectAsync(new PlcConnectionConfig(Host: "192.168.1.10", Port: 502), ct);
/// byte[] raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 4, ct); // 레지스터 2개
/// await driver.WriteAsync(address: 0, data: raw, ct);
///
/// // 2) RTU 모드 — 직접 연결
/// IProtocolDriver rtuDriver = new ModbusDriver(isRtu: true, unitId: 1);
/// await rtuDriver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM3", BaudRate: 19200), ct);
/// byte[] rtuRaw = await rtuDriver.ReadAsync(startAddress: 0, lengthBytes: 2, ct);
///
/// // 3) RT-10 SharedResourceManager로 공유 — 같은 PLC를 참조하는 TagNode 2개가 인스턴스 1개를 공유
/// var manager = new SharedResourceManager();
/// var config = new PlcConnectionConfig(Host: "192.168.1.10", Port: 502);
/// var d1 = await manager.AcquireAsync("plc-1", () => new ModbusDriver(id: "plc-1", config: config), ct); // TagNode #1
/// var d2 = await manager.AcquireAsync("plc-1", () => new ModbusDriver(id: "plc-1", config: config), ct); // TagNode #2, d1과 동일 인스턴스
///
/// // 4) (PD-01c, ★ 추가) Virtual 모드 — 실제 하드웨어 없이 VirtualModbusSlave에 연결
/// var slave = new VirtualModbusSlave();
/// slave.SetRegister(0, 0x1234);
/// using var virtualDriver = new ModbusDriver { VirtualSlave = slave };
/// await virtualDriver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), ct); // Host/Port는 쓰이지 않음
/// byte[] virtualRaw = await virtualDriver.ReadAsync(startAddress: 0, lengthBytes: 2, ct); // { 0x12, 0x34 }
/// </code>
/// </example>
public sealed class ModbusDriver : IProtocolDriver, ISharedServiceNode, IDisposable
{
    private const byte FunctionReadHoldingRegisters = 0x03;
    private const byte FunctionWriteSingleRegister = 0x06;
    private const byte FunctionWriteMultipleRegisters = 0x10;
    private const byte ExceptionFlag = 0x80;

    private readonly bool _isRtu;
    private readonly byte _unitId;
    private TcpClient? _tcpClient;
    private SerialPort? _serialPort;
    private Stream? _stream;
    private ushort _nextTransactionId;

    /// <summary>
    /// <paramref name="isRtu"/>가 true면 RTU(Serial) 모드로, false(기본값)면 TCP 모드로 연결합니다.
    /// <paramref name="unitId"/>는 TCP에서는 MBAP 헤더의 Unit Identifier, RTU에서는 슬레이브 주소로
    /// 쓰이는 같은 개념의 값입니다(기본 1 — 대부분의 Modbus 게이트웨이/슬레이브가 쓰는 관례값).
    /// (PD-01b, ★ 추가) <paramref name="id"/>/<paramref name="config"/>는 <see cref="ISharedServiceNode"/>로
    /// RT-10 <c>SharedResourceManager</c>에 공유 관리될 때만 필요합니다 — 기존처럼 <see cref="ConnectAsync"/>를
    /// 직접 호출하는 사용법에서는 지정하지 않아도 됩니다.
    /// </summary>
    public ModbusDriver(bool isRtu = false, byte unitId = 1, string id = "", PlcConnectionConfig? config = null)
    {
        _isRtu = isRtu;
        _unitId = unitId;
        Id = id;
        Config = config;
    }

    /// <inheritdoc />
    public string Type => _isRtu ? ProtocolDriverType.ModbusRtu : ProtocolDriverType.ModbusTcp;

    /// <summary>
    /// (PD-01b, ★ 추가, <see cref="ISharedServiceNode.Id"/>) 이 드라이버를 RT-10 <c>SharedResourceManager</c>로
    /// 공유 관리할 때 쓰는 식별자입니다. 같은 PLC(같은 접속 정보)를 참조하는 TagNode 여러 개는 항상
    /// 같은 Id를 써야 인스턴스가 하나로 공유됩니다. <see cref="Config"/>가 없으면 <see cref="StartAsync"/>가
    /// 예외를 던지므로, SharedResourceManager를 거치지 않는 기존 사용법에서는 비워둬도 무방합니다.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// (PD-01b, ★ 추가) <see cref="StartAsync"/>가 <see cref="ConnectAsync"/>에 그대로 전달할 접속
    /// 설정입니다. <c>SharedResourceManager.AcquireAsync</c>의 factory로 이 드라이버를 생성할 때만
    /// 지정하면 됩니다 — 기존처럼 <see cref="ConnectAsync(PlcConnectionConfig, CancellationToken)"/>을
    /// 직접 호출하는 사용법(PD-01a 테스트 등)에서는 필요 없습니다.
    /// </summary>
    public PlcConnectionConfig? Config { get; init; }

    /// <summary>
    /// (PD-01b, 테스트 전용 확장점) RTU 모드에서 실제 <see cref="SerialPort"/>를 여는 대신 테스트가
    /// 주입하는 가짜 <see cref="Stream"/>을 쓰기 위한 시드입니다. null(기본값)이면 <see cref="ConnectAsync"/>가
    /// 실제 COM 포트를 엽니다 — 실제 COM 포트가 없는 빌드/CI 환경에서도 CRC16·ADU 프레이밍 로직을
    /// xUnit으로 검증할 수 있도록(TCP 쪽 <c>FakeModbusTcpSlave</c>가 실제 루프백 소켓을 쓰는 것과 동일한
    /// 취지 — 목이 아니라 실제 바이트 왕복 경로를 태움) 테스트 프로젝트에만 <c>InternalsVisibleTo</c>로
    /// 공개합니다.
    /// </summary>
    internal Func<PlcConnectionConfig, CancellationToken, Task<Stream>>? RtuStreamFactory { get; set; }

    /// <summary>
    /// (PD-01c, ★ 추가) 설정하면 <see cref="ConnectAsync"/>가 실제 TCP/RTU 대신 이 가상 슬레이브에
    /// 인메모리로 연결합니다 — 실제 하드웨어 없이 개발·테스트할 때 씁니다(클래스 remarks
    /// "VirtualSlave — 가상 Modbus 슬레이브 연결" 참고). <c>isRtu: true</c>와 함께 설정하면
    /// <see cref="ConnectAsync"/>가 <see cref="ArgumentException"/>을 던집니다 — <see cref="VirtualModbusSlave"/>는
    /// MBAP(TCP) 프레이밍만 지원하기 때문입니다.
    /// </summary>
    public VirtualModbusSlave? VirtualSlave { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// (PD-01b, ★ 변경) RTU 모드는 더 이상 <see cref="NotSupportedException"/>을 던지지 않고
    /// <paramref name="config"/>.<see cref="PlcConnectionConfig.ComPort"/>로 실제 <see cref="SerialPort"/>를
    /// 엽니다(8N1, 클래스 remarks의 "RTU 시리얼 설정 고정값" 참고). <c>ComPort</c>가 비어 있으면
    /// <see cref="ArgumentException"/>을 던집니다. (PD-01c, ★ 추가) <see cref="VirtualSlave"/>가 설정돼
    /// 있으면 실제 TCP/RTU 대신 그 가상 슬레이브에 연결합니다 — <c>isRtu: true</c>와 함께 쓰면
    /// <see cref="ArgumentException"/>을 던집니다.
    /// </remarks>
    public async Task ConnectAsync(PlcConnectionConfig config, CancellationToken ct)
    {
        Disconnect();

        if (VirtualSlave is not null)
        {
            if (_isRtu)
            {
                throw new ArgumentException("VirtualModbusSlave는 MBAP(TCP) 프레이밍만 지원합니다 — isRtu: true와 함께 쓸 수 없습니다.", nameof(config));
            }

            _stream = VirtualSlave.Connect();
            return;
        }

        if (_isRtu)
        {
            if (RtuStreamFactory is not null)
            {
                _stream = await RtuStreamFactory(config, ct).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(config.ComPort))
            {
                throw new ArgumentException("Modbus RTU 접속에는 PlcConnectionConfig.ComPort가 필요합니다(예: \"COM3\").", nameof(config));
            }

            var port = new SerialPort(config.ComPort, config.BaudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReadTimeout = 3000,
                WriteTimeout = 3000,
            };
            port.Open();
            _serialPort = port;
            _stream = port.BaseStream;
            return;
        }

        var client = new TcpClient();
        await client.ConnectAsync(config.Host, config.Port, ct).ConfigureAwait(false);
        _tcpClient = client;
        _stream = client.GetStream();
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(int startAddress, int lengthBytes, CancellationToken ct)
    {
        var stream = EnsureConnected();

        if (lengthBytes <= 0 || lengthBytes % 2 != 0)
        {
            throw new ArgumentException("Modbus Holding Register는 2바이트 단위입니다 — lengthBytes는 2의 배수인 양수여야 합니다.", nameof(lengthBytes));
        }

        ushort quantity = checked((ushort)(lengthBytes / 2));
        var pdu = new byte[5];
        pdu[0] = FunctionReadHoldingRegisters;
        WriteUInt16BigEndian(pdu, 1, checked((ushort)startAddress));
        WriteUInt16BigEndian(pdu, 3, quantity);

        var response = await SendAndReceiveAsync(stream, pdu, ct).ConfigureAwait(false);
        // 응답 PDU = [함수코드(1)][바이트수(1)][데이터(바이트수)]
        var byteCount = response[1];
        var data = new byte[byteCount];
        Array.Copy(response, 2, data, 0, byteCount);
        return data;
    }

    /// <inheritdoc />
    public async Task WriteAsync(int address, byte[] data, CancellationToken ct)
    {
        var stream = EnsureConnected();

        if (data.Length == 0 || data.Length % 2 != 0)
        {
            throw new ArgumentException("Modbus Holding Register는 2바이트 단위입니다 — data.Length는 2의 배수인 양수여야 합니다.", nameof(data));
        }

        byte[] pdu;
        if (data.Length == 2)
        {
            // FC06 — Write Single Register: [함수코드(1)][주소(2)][값(2)]
            pdu = new byte[5];
            pdu[0] = FunctionWriteSingleRegister;
            WriteUInt16BigEndian(pdu, 1, checked((ushort)address));
            pdu[3] = data[0];
            pdu[4] = data[1];
        }
        else
        {
            // FC16(0x10) — Write Multiple Registers: [함수코드(1)][시작주소(2)][레지스터수(2)][바이트수(1)][값(N)]
            ushort quantity = checked((ushort)(data.Length / 2));
            pdu = new byte[6 + data.Length];
            pdu[0] = FunctionWriteMultipleRegisters;
            WriteUInt16BigEndian(pdu, 1, checked((ushort)address));
            WriteUInt16BigEndian(pdu, 3, quantity);
            pdu[5] = checked((byte)data.Length);
            Array.Copy(data, 0, pdu, 6, data.Length);
        }

        await SendAndReceiveAsync(stream, pdu, ct).ConfigureAwait(false);
        // 응답(에코 또는 시작주소+레지스터수 확인)은 정상 응답 여부만 판단하는 데 쓰이고 별도 반환값은
        // 없습니다 — WriteAsync 자체가 Task(무반환)라 SendAndReceiveAsync가 예외 응답이면 이미
        // ModbusException을 던진 뒤이므로, 여기까지 왔으면 쓰기가 성공한 것입니다.
    }

    /// <summary>(PD-01b, ★ 추가) <see cref="_isRtu"/> 여부로 TCP(MBAP)/RTU(CRC16) 겉봉투 처리를 분기하는 공통 진입점입니다. PDU 조립은 <see cref="ReadAsync"/>/<see cref="WriteAsync"/>가 이미 마쳤으므로 여기서는 겉봉투만 다룹니다.</summary>
    private Task<byte[]> SendAndReceiveAsync(Stream stream, byte[] pdu, CancellationToken ct) =>
        _isRtu ? SendAndReceiveRtuAsync(stream, pdu, ct) : SendAndReceiveTcpAsync(stream, pdu, ct);

    /// <summary>MBAP 헤더(6바이트: TransactionId·ProtocolId=0·Length)를 붙여 <paramref name="pdu"/>를 보내고, 같은 형식의 응답 MBAP+PDU를 읽어 PDU만 반환합니다. 응답이 Modbus 예외(함수 코드 최상위 비트 설정)면 <see cref="ModbusException"/>을 던집니다.</summary>
    private async Task<byte[]> SendAndReceiveTcpAsync(Stream stream, byte[] pdu, CancellationToken ct)
    {
        var transactionId = _nextTransactionId++;

        var request = new byte[6 + 1 + pdu.Length];
        WriteUInt16BigEndian(request, 0, transactionId);
        WriteUInt16BigEndian(request, 2, 0); // Protocol Id — Modbus는 항상 0
        WriteUInt16BigEndian(request, 4, checked((ushort)(1 + pdu.Length))); // Length = Unit Id(1) + PDU
        request[6] = _unitId;
        Array.Copy(pdu, 0, request, 7, pdu.Length);

        await stream.WriteAsync(request, ct).ConfigureAwait(false);

        var header = new byte[6];
        await ReadExactAsync(stream, header, ct).ConfigureAwait(false);
        var remainingLength = ReadUInt16BigEndian(header, 4);
        if (remainingLength < 1)
        {
            throw new ModbusException("Modbus 응답 MBAP Length 필드가 유효하지 않습니다(Unit Id를 포함할 최소 1 이상이어야 함).", exceptionCode: null);
        }

        var body = new byte[remainingLength];
        await ReadExactAsync(stream, body, ct).ConfigureAwait(false);
        // body[0] = Unit Id, body[1..] = 응답 PDU
        var responsePdu = new byte[body.Length - 1];
        Array.Copy(body, 1, responsePdu, 0, responsePdu.Length);

        if ((responsePdu[0] & ExceptionFlag) != 0)
        {
            var exceptionCode = responsePdu.Length > 1 ? responsePdu[1] : (byte)0;
            throw new ModbusException($"Modbus 슬레이브가 예외 응답을 반환했습니다(함수 코드 0x{(responsePdu[0] & ~ExceptionFlag):X2}, 예외 코드 0x{exceptionCode:X2}).", exceptionCode);
        }

        return responsePdu;
    }

    /// <summary>
    /// (PD-01b, ★ 추가) [슬레이브 주소(1)]+<paramref name="pdu"/>에 CRC16(2바이트, Low-High 순)을 붙여
    /// 보내고, 같은 형식의 응답을 읽어 CRC를 검증한 뒤 PDU([함수코드]+본문, TCP 응답 PDU와 동일한
    /// 형태)만 반환합니다. 응답 함수 코드는 요청과 동일해야 정상 응답 본문 길이를 알 수 있으므로
    /// <paramref name="pdu"/>[0](요청 함수 코드)을 그대로 참고합니다. CRC가 일치하지 않으면 계산이
    /// 손상된 응답을 신뢰하지 않고 즉시 <see cref="ModbusException"/>을 던져 거부합니다(완료 기준
    /// "CRC 오류 응답은 거부되고 정상 응답만 반영") — 슬레이브 예외 응답(함수 코드 최상위 비트)도
    /// CRC 검증을 먼저 통과해야만 <see cref="ModbusException"/>(예외 코드 포함)으로 변환합니다.
    /// </summary>
    private async Task<byte[]> SendAndReceiveRtuAsync(Stream stream, byte[] pdu, CancellationToken ct)
    {
        var requestFunctionCode = pdu[0];

        var frame = new byte[1 + pdu.Length + 2];
        frame[0] = _unitId; // RTU에서는 Unit Id가 곧 슬레이브 주소(프레임 맨 앞 1바이트)
        Array.Copy(pdu, 0, frame, 1, pdu.Length);
        var requestCrc = ComputeCrc16(frame.AsSpan(0, frame.Length - 2));
        frame[^2] = (byte)requestCrc;
        frame[^1] = (byte)(requestCrc >> 8);

        await stream.WriteAsync(frame, ct).ConfigureAwait(false);

        // [슬레이브 주소(1)][함수 코드(1)]까지 먼저 읽어 정상/예외 응답과 뒤이어 올 본문 길이를 판단합니다.
        var header = new byte[2];
        await ReadExactAsync(stream, header, ct).ConfigureAwait(false);
        var responseFunctionCode = header[1];

        byte[] body; // CRC 앞까지, 함수 코드 다음에 오는 가변 길이 본문
        if ((responseFunctionCode & ExceptionFlag) != 0)
        {
            body = new byte[1]; // 예외 코드 1바이트
            await ReadExactAsync(stream, body, ct).ConfigureAwait(false);
        }
        else if (requestFunctionCode == FunctionReadHoldingRegisters)
        {
            var byteCountBuf = new byte[1];
            await ReadExactAsync(stream, byteCountBuf, ct).ConfigureAwait(false);
            body = new byte[1 + byteCountBuf[0]];
            body[0] = byteCountBuf[0];
            await ReadExactAsync(stream, body.AsMemory(1), ct).ConfigureAwait(false);
        }
        else
        {
            // FC06(에코: 주소2+값2)/FC16(시작주소2+레지스터수2) 정상 응답 본문은 둘 다 4바이트 고정
            body = new byte[4];
            await ReadExactAsync(stream, body, ct).ConfigureAwait(false);
        }

        var crcBuf = new byte[2];
        await ReadExactAsync(stream, crcBuf, ct).ConfigureAwait(false);
        var receivedCrc = (ushort)(crcBuf[0] | (crcBuf[1] << 8));

        var frameForCrc = new byte[header.Length + body.Length];
        header.CopyTo(frameForCrc, 0);
        body.CopyTo(frameForCrc, header.Length);
        var computedCrc = ComputeCrc16(frameForCrc);
        if (computedCrc != receivedCrc)
        {
            throw new ModbusException($"Modbus RTU 응답 CRC가 일치하지 않습니다(계산값 0x{computedCrc:X4}, 수신값 0x{receivedCrc:X4}) — 오류 응답으로 간주해 거부합니다.", exceptionCode: null);
        }

        if ((responseFunctionCode & ExceptionFlag) != 0)
        {
            var exceptionCode = body[0];
            throw new ModbusException($"Modbus 슬레이브가 예외 응답을 반환했습니다(함수 코드 0x{(responseFunctionCode & ~ExceptionFlag):X2}, 예외 코드 0x{exceptionCode:X2}).", exceptionCode);
        }

        // 정상 응답 PDU = [함수 코드] + body — TCP 경로(SendAndReceiveTcpAsync)의 응답 PDU와 같은 형태로
        // 맞춰 ReadAsync/WriteAsync가 모드와 무관하게 동일하게 처리할 수 있게 합니다.
        var responsePdu = new byte[1 + body.Length];
        responsePdu[0] = responseFunctionCode;
        body.CopyTo(responsePdu, 1);
        return responsePdu;
    }

    /// <summary>
    /// (PD-01b, ★ 추가) Modbus RTU 표준 CRC16(다항식 0xA001, 초기값 0xFFFF)을 계산합니다. 전송 시에는
    /// 결과를 Low바이트 먼저, High바이트 나중 순서로 프레임 끝에 붙입니다(<see cref="SendAndReceiveRtuAsync"/>).
    /// </summary>
    private static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }

    /// <summary><paramref name="buffer"/>가 가득 찰 때까지 <paramref name="stream"/>에서 반복해서 읽습니다 — TCP/RTU 모두 스트림 기반이라 한 번의 <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> 호출이 요청한 바이트 수보다 적게 반환할 수 있기 때문입니다.</summary>
    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new ModbusException("Modbus 슬레이브와의 연결이 응답을 다 받기 전에 끊겼습니다.", exceptionCode: null);
            }

            offset += read;
        }
    }

    private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16BigEndian(byte[] buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    /// <summary>연결돼 있지 않으면(<see cref="ConnectAsync"/> 미호출) <see cref="InvalidOperationException"/>을 던지고, 연결돼 있으면 그 <see cref="Stream"/>을 반환합니다.</summary>
    private Stream EnsureConnected() =>
        _stream ?? throw new InvalidOperationException("ConnectAsync를 먼저 호출해야 합니다.");

    private void Disconnect()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _serialPort?.Dispose();
        _stream = null;
        _tcpClient = null;
        _serialPort = null;
    }

    /// <summary>연결을 정리합니다 — <see cref="IProtocolDriver"/>는 <see cref="IDisposable"/>을 요구하지 않지만, 소켓/시리얼 포트 리소스를 쥐고 있으므로 이 구현체는 추가로 <see cref="IDisposable"/>도 구현합니다.</summary>
    public void Dispose() => Disconnect();

    /// <summary>
    /// (PD-01b, ★ 추가, <see cref="ISharedServiceNode.StartAsync"/>) <c>SharedResourceManager</c>가 이
    /// 드라이버를 처음 Acquire할 때 1회만 호출합니다(RT-10 참조 카운트 원칙 — 같은 Id를 참조하는 이후
    /// TagNode들은 이미 연결된 이 인스턴스를 그대로 재사용). <see cref="Config"/>가 설정돼 있지 않으면
    /// <see cref="InvalidOperationException"/>을 던집니다.
    /// </summary>
    public Task StartAsync(CancellationToken ct) =>
        ConnectAsync(Config ?? throw new InvalidOperationException("SharedResourceManager로 공유 관리하려면 ModbusDriver.Config를 지정해야 합니다."), ct);

    /// <summary>
    /// (PD-01b, ★ 추가, <see cref="ISharedServiceNode.StopAsync"/>) 이 드라이버(=이 PLC 연결)를 참조하던
    /// 마지막 TagNode가 배포에서 사라질 때 <c>SharedResourceManager</c>가 1회만 호출합니다.
    /// </summary>
    public Task StopAsync()
    {
        Disconnect();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Class명 : Modbus 예외
/// 역활 및 기능 : Modbus 슬레이브가 반환한 예외 응답(또는 그 밖의 프레이밍/CRC 오류)을 나타내는 예외
///
/// <see cref="ModbusDriver"/>가 슬레이브의 Modbus 예외 응답(함수 코드 최상위 비트 설정, 1바이트 예외
/// 코드)을 감지하거나, 응답 프레이밍이 손상됐을 때(연결이 응답 도중 끊김 등) 이 예외를 던집니다.
/// (PD-01b, ★ 추가) RTU 모드에서는 CRC16 불일치도 이 예외로 거부합니다(<see cref="ExceptionCode"/>는
/// 이 경우 <c>null</c> — 슬레이브가 보낸 정식 예외 코드가 아니라 프레이밍/전송 오류이기 때문).
/// </summary>
public sealed class ModbusException : Exception
{
    /// <summary>슬레이브가 반환한 Modbus 예외 코드(예: 0x02 = Illegal Data Address). 프레이밍/CRC 오류처럼 슬레이브 예외 응답이 아닌 경우 <c>null</c>.</summary>
    public byte? ExceptionCode { get; }

    public ModbusException(string message, byte? exceptionCode) : base(message) => ExceptionCode = exceptionCode;
}
