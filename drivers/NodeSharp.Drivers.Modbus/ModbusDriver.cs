using System.Net.Sockets;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Drivers.Modbus;

/// <summary>
/// Class명 : Modbus 프로토콜 드라이버
/// 역활 및 기능 : Modbus TCP(MBAP 헤더) 위에서 Holding Register 읽기/쓰기를 수행하는 <see cref="IProtocolDriver"/> 1차 구현체
///
/// (PD-01a) 02번 설계문서 11번 탭 카드8이 예시로 든 <c>new ModbusDriver(IsRtu: false)</c> 그대로,
/// TCP 모드(<see cref="ProtocolDriverType.ModbusTcp"/>)를 구현합니다. Serial 기반 RTU 모드
/// (<see cref="ProtocolDriverType.ModbusRtu"/>)는 별도 Step(PD-01b)에서 이 클래스에 이어서 구현할
/// 예정이라 지금은 <see cref="ConnectAsync"/>가 <see cref="NotSupportedException"/>을 던집니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>NetTransportType.Tcp 대신 System.Net.Sockets.TcpClient를 직접 사용</b>: 03번 Step맵의
/// PD-01a 설명은 "LL-05a로 포팅된 NodeSharp.Util.Net의 NetTransportType.Tcp 위에서"라고 적혀 있지만,
/// LL-05a(lssLib.Net 9종 Transport 포팅, Phase 12)는 이 시점(Phase 9)에 아직 ⏳ 대기 상태로 실제
/// 코드가 전혀 없습니다(<c>NodeSharp.Util.Net</c> 네임스페이스·<c>INetTransport</c> 어디에도 존재하지
/// 않음, 저장소 전체 검색으로 확인). ED-D04(TagRef 연동)가 이 드라이버의 존재를 전제한다는 설계
/// 순서 메모(PD-01a 행 desc, "표 위치≠실행 순서")를 지키기 위해, LL-05a를 먼저 통째로 구현하는
/// 대신 이 클래스 내부에서 최소한의 TCP 소켓 처리(MBAP 프레이밍)만 직접 수행합니다 — LL-05a가
/// 실제로 구현되면 그 추상화를 쓰도록 리팩터링할 예정이며, 그때까지는 이 방식이 임시 구현임을
/// 여기 남겨둡니다.</item>
/// <item><b>주소 규약</b>: <see cref="ReadAsync"/>/<see cref="WriteAsync"/>의 <c>address</c>는
/// <see cref="IProtocolDriver"/> 인터페이스 문서의 예시(<c>ReadAsync(startAddress: 40001, ...)</c>)를
/// 그대로 따라 <b>Modbus 프로토콜 주소 필드에 그대로 실리는 값</b>입니다 — 전통적인 "40001 = 0번지"
/// 오프셋 변환은 하지 않습니다(그런 변환 규칙이 설계 문서 어디에도 정의돼 있지 않음). 호출 측
/// (<c>DeviceMapNode.StartAddress</c>, 8번 탭)이 실제 프로토콜 주소를 그대로 넘겨야 합니다.</item>
/// <item><b>레지스터 단위</b>: Modbus Holding Register는 2바이트 고정폭이라 <c>lengthBytes</c>(읽기)와
/// <c>data.Length</c>(쓰기)는 반드시 2의 배수여야 합니다 — 아니면 <see cref="ArgumentException"/>.</item>
/// <item><b>쓰기 함수 코드 선택</b>: <c>data.Length == 2</c>(레지스터 1개)면 FC06(Write Single
/// Register), 그 이상이면 FC16/0x10(Write Multiple Registers)을 사용합니다 — 03번 Step맵 PD-01a
/// 설명의 "0x06·0x10 Write" 둘 다를 구현했습니다.</item>
/// <item><b>예외 응답</b>: 슬레이브가 Modbus 예외 응답(함수 코드 최상위 비트 설정, 1바이트 예외
/// 코드)을 보내면 <see cref="ModbusException"/>을 던집니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// IProtocolDriver driver = new ModbusDriver();
/// await driver.ConnectAsync(new PlcConnectionConfig(Host: "192.168.1.10", Port: 502), ct);
///
/// byte[] raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 4, ct); // 레지스터 2개
/// await driver.WriteAsync(address: 0, data: raw, ct);
/// </code>
/// </example>
public sealed class ModbusDriver : IProtocolDriver, IDisposable
{
    private const byte FunctionReadHoldingRegisters = 0x03;
    private const byte FunctionWriteSingleRegister = 0x06;
    private const byte FunctionWriteMultipleRegisters = 0x10;
    private const byte ExceptionFlag = 0x80;

    private readonly bool _isRtu;
    private readonly byte _unitId;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _nextTransactionId;

    /// <summary>
    /// <paramref name="isRtu"/>가 true면 RTU(Serial) 모드를 요구하는 것이지만, 이 Step(PD-01a)은
    /// TCP 모드만 구현합니다 — RTU는 <see cref="ConnectAsync"/>에서 <see cref="NotSupportedException"/>으로
    /// 즉시 알립니다(PD-01b 예정). <paramref name="unitId"/>는 MBAP 헤더의 Unit Identifier(슬레이브
    /// 주소, 기본 1 — 대부분의 Modbus TCP 게이트웨이가 쓰는 관례값).
    /// </summary>
    public ModbusDriver(bool isRtu = false, byte unitId = 1)
    {
        _isRtu = isRtu;
        _unitId = unitId;
    }

    /// <inheritdoc />
    public string Type => _isRtu ? ProtocolDriverType.ModbusRtu : ProtocolDriverType.ModbusTcp;

    /// <inheritdoc />
    public async Task ConnectAsync(PlcConnectionConfig config, CancellationToken ct)
    {
        if (_isRtu)
        {
            throw new NotSupportedException("Modbus RTU 모드는 PD-01b에서 구현 예정입니다 — 이 Step(PD-01a)은 TCP 모드만 지원합니다.");
        }

        Disconnect();

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

    /// <summary>MBAP 헤더(6바이트: TransactionId·ProtocolId=0·Length)를 붙여 <paramref name="pdu"/>를 보내고, 같은 형식의 응답 MBAP+PDU를 읽어 PDU만 반환합니다. 응답이 Modbus 예외(함수 코드 최상위 비트 설정)면 <see cref="ModbusException"/>을 던집니다.</summary>
    private async Task<byte[]> SendAndReceiveAsync(NetworkStream stream, byte[] pdu, CancellationToken ct)
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

    /// <summary><paramref name="buffer"/>가 가득 찰 때까지 <paramref name="stream"/>에서 반복해서 읽습니다 — TCP는 스트림 기반이라 한 번의 <see cref="NetworkStream.ReadAsync(Memory{byte}, CancellationToken)"/> 호출이 요청한 바이트 수보다 적게 반환할 수 있기 때문입니다.</summary>
    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
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

    /// <summary>연결돼 있지 않으면(<see cref="ConnectAsync"/> 미호출) <see cref="InvalidOperationException"/>을 던지고, 연결돼 있으면 그 <see cref="NetworkStream"/>을 반환합니다.</summary>
    private NetworkStream EnsureConnected() =>
        _stream ?? throw new InvalidOperationException("ConnectAsync를 먼저 호출해야 합니다.");

    private void Disconnect()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _stream = null;
        _tcpClient = null;
    }

    /// <summary>TCP 연결을 정리합니다 — <see cref="IProtocolDriver"/>는 <see cref="IDisposable"/>을 요구하지 않지만, 소켓 리소스를 쥐고 있으므로 이 구현체는 추가로 <see cref="IDisposable"/>도 구현합니다.</summary>
    public void Dispose() => Disconnect();
}

/// <summary>
/// Class명 : Modbus 예외
/// 역활 및 기능 : Modbus 슬레이브가 반환한 예외 응답(또는 그 밖의 프레이밍 오류)을 나타내는 예외
///
/// <see cref="ModbusDriver"/>가 슬레이브의 Modbus 예외 응답(함수 코드 최상위 비트 설정, 1바이트 예외
/// 코드)을 감지하거나, 응답 프레이밍이 손상됐을 때(연결이 응답 도중 끊김 등) 이 예외를 던집니다.
/// </summary>
public sealed class ModbusException : Exception
{
    /// <summary>슬레이브가 반환한 Modbus 예외 코드(예: 0x02 = Illegal Data Address). 프레이밍 오류처럼 슬레이브 예외 응답이 아닌 경우 <c>null</c>.</summary>
    public byte? ExceptionCode { get; }

    public ModbusException(string message, byte? exceptionCode) : base(message) => ExceptionCode = exceptionCode;
}
