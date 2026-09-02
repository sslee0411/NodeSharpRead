namespace NodeSharp.Drivers.Modbus;

/// <summary>
/// Class명 : 가상 Modbus 슬레이브
/// 역활 및 기능 : 메모리 기반 레지스터 배열로 동작하는 개발·테스트용 가상 Modbus TCP 슬레이브(NetTransportType.Virtual)
///
/// (PD-01c) 02번 설계문서 11번 탭 카드1이 예고한 <c>NetTransportType.Virtual</c>("실제 네트워크/하드웨어
/// 없이 개발·테스트할 때 VirtualModbusSlave 등이 사용하는 시뮬레이터용") 용도를 실제로 구현합니다.
/// <see cref="ModbusDriver.VirtualSlave"/>에 이 인스턴스를 설정하면 <see cref="ModbusDriver.ConnectAsync"/>가
/// 실제 TCP 소켓 대신 이 슬레이브에 인메모리로 연결해, <see cref="ModbusDriver"/> 입장에서는 실제 Modbus
/// TCP 슬레이브와 구분되지 않게 동작합니다(FC03/FC06/FC16 MBAP 프레이밍을 그대로 처리) — 03번 Step맵
/// PD-01c desc의 "ModbusDriver(PD-01a/b) 입장에서는 실제 Tcp/Serial과 구분되지 않게 동작" 요구를 충족.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>범위 축소(사용자 확인 — "핵심만 우선 구현", 2026-09-01)</b>: 완료 기준이 요구하는 "PlcNode
/// 속성창 시뮬레이션 모드 체크박스"·"Editor 시뮬레이터 패널"·"캔버스 PlcTagReadNode 출력 반영"은 Editor
/// UI·SignalR 실시간 파이프라인에 걸친 별도 작업이라 후속 Step(PD-01d)으로 분리했습니다 — 이 Step은
/// ModbusDriver가 실제로 Virtual Transport로 동작하는 핵심 시뮬레이터 자체만 구현합니다.</item>
/// <item><b>MBAP(TCP) 프레이밍만 지원</b>: <c>NetTransportType.Virtual</c>은 RTU CRC16까지 시뮬레이션할
/// 필요가 완료 기준에 없어(PD-01b RTU 경로는 이미 실제 <see cref="System.IO.Ports.SerialPort"/>로 별도
/// 검증됨), 항상 TCP와 동일한 MBAP 프레이밍으로 응답합니다 — <see cref="ModbusDriver.ConnectAsync"/>는
/// <see cref="ModbusDriver.VirtualSlave"/>가 설정된 채로 <c>isRtu=true</c>면 <see cref="ArgumentException"/>을
/// 던져 이 제약을 명확히 합니다.</item>
/// <item><b>동시 연결 1개</b>: 이 클래스는 개발·테스트용 단순 시뮬레이터라 <see cref="Connect"/>를 두 번
/// 부르면(=ModbusDriver 2개가 같은 가상 슬레이브에 새로 연결하려 하면) 이전 연결을 끊고 새 연결로
/// 교체합니다 — 실제 PLC 게이트웨이가 흔히 갖는 "동시접속 1개" 제약과 유사하게 단순화했습니다. 여러
/// TagNode가 같은 PLC를 공유하는 시나리오는 RT-10 <c>SharedResourceManager</c>가 <see cref="ModbusDriver"/>
/// 인스턴스 자체를 공유해 애초에 <see cref="Connect"/>가 여러 번 불릴 필요가 없습니다(PD-01b
/// <c>ModbusDriverSharedResourceTests.cs</c> 참고).</item>
/// <item><b>레지스터 주소 공간</b>: Modbus 표준 주소 공간(0~65535, 2바이트) 전체를 배열로 미리 확보합니다
/// — <see cref="ModbusDriver"/>가 전통적 오프셋 변환 없이 프로토콜 주소를 그대로 쓰는 것과 동일한 원칙
/// (ModbusDriver.cs 클래스 문서 "주소 규약" 참고).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var slave = new VirtualModbusSlave();
/// slave.SetRegister(0, 0x1234);
///
/// using var driver = new ModbusDriver { VirtualSlave = slave };
/// // Virtual 모드에서는 Host/Port/ComPort 값이 쓰이지 않지만 ConnectAsync 시그니처는 그대로 따름
/// await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);
/// var raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None); // { 0x12, 0x34 }
///
/// await driver.WriteAsync(address: 1, data: new byte[] { 0x00, 0x2A }, CancellationToken.None);
/// slave.GetRegister(1); // 0x002A — 시뮬레이터 쪽에서도 곧바로 확인 가능(향후 PD-01d 시뮬레이터 패널의 기반)
/// </code>
/// </example>
public sealed class VirtualModbusSlave
{
    private const byte FunctionReadHoldingRegisters = 0x03;
    private const byte FunctionWriteSingleRegister = 0x06;
    private const byte FunctionWriteMultipleRegisters = 0x10;
    private const byte ExceptionFlag = 0x80;

    private readonly ushort[] _registers = new ushort[65536]; // Modbus 주소 공간 최대치(0~65535)만큼 확보
    private readonly object _connectLock = new();
    private CancellationTokenSource? _currentConnectionCts;

    /// <summary>주소 <paramref name="address"/>에 레지스터 값을 미리 채웁니다(연결 전/후 아무 때나 가능 — 다음 FC03 응답에 즉시 반영).</summary>
    public void SetRegister(int address, ushort value) => _registers[address] = value;

    /// <summary>주소 <paramref name="address"/>의 현재 레지스터 값을 읽습니다(연결된 ModbusDriver가 FC06/16으로 쓴 값도 즉시 반영).</summary>
    public ushort GetRegister(int address) => _registers[address];

    /// <summary>
    /// 새 연결을 열어 <see cref="ModbusDriver"/>가 쓸 <see cref="Stream"/>을 반환합니다. 이미 연결이 있으면
    /// 먼저 끊습니다(클래스 remarks "동시 연결 1개" 참고). 백그라운드에서 FC03/06/16 요청/응답 루프가
    /// 즉시 시작됩니다.
    /// </summary>
    public Stream Connect()
    {
        lock (_connectLock)
        {
            _currentConnectionCts?.Cancel();

            var (driverSide, slaveSide) = InMemoryDuplexStream.CreatePair();
            var cts = new CancellationTokenSource();
            _currentConnectionCts = cts;
            _ = RunAsync(slaveSide, cts.Token);
            return driverSide;
        }
    }

    /// <summary>연결 하나짜리 MBAP 요청/응답 루프 — 스트림이 끊기거나 <see cref="Connect"/>가 다시 불려 취소될 때까지 돕니다.</summary>
    private async Task RunAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var header = new byte[6];
                if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
                {
                    return;
                }

                var length = (header[4] << 8) | header[5];
                var body = new byte[length];
                if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
                {
                    return;
                }

                var unitId = body[0];
                var requestPdu = body.AsSpan(1).ToArray();
                var responsePdu = HandlePdu(requestPdu);

                var responseBody = new byte[1 + responsePdu.Length];
                responseBody[0] = unitId;
                Array.Copy(responsePdu, 0, responseBody, 1, responsePdu.Length);

                var responseHeader = new byte[6];
                responseHeader[0] = header[0];
                responseHeader[1] = header[1]; // Transaction Id를 그대로 에코(ModbusDriver.cs SendAndReceiveTcpAsync와 동일 규약)
                responseHeader[4] = (byte)(responseBody.Length >> 8);
                responseHeader[5] = (byte)responseBody.Length;

                await stream.WriteAsync(responseHeader, ct).ConfigureAwait(false);
                await stream.WriteAsync(responseBody, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Connect()가 다시 불려 이전 연결을 취소한 경우 — 정상 종료 경로.
        }
        catch (IOException)
        {
            // 연결된 ModbusDriver가 먼저 Dispose된 경우 — 정상 종료 경로.
        }
    }

    /// <summary>FC03(Read Holding Registers)/FC06(Write Single Register)/FC16(Write Multiple Registers) 요청을 처리해 응답 PDU를 만듭니다 — ModbusDriverTests.FakeModbusTcpSlave와 동일한 처리 규약(주소는 오프셋 변환 없이 그대로).</summary>
    private byte[] HandlePdu(byte[] pdu)
    {
        var functionCode = pdu[0];

        if (functionCode == FunctionReadHoldingRegisters)
        {
            var start = (pdu[1] << 8) | pdu[2];
            var quantity = (pdu[3] << 8) | pdu[4];
            var data = new byte[quantity * 2];
            for (var i = 0; i < quantity; i++)
            {
                var value = _registers[start + i];
                data[i * 2] = (byte)(value >> 8);
                data[(i * 2) + 1] = (byte)value;
            }

            var response = new byte[2 + data.Length];
            response[0] = functionCode;
            response[1] = (byte)data.Length;
            Array.Copy(data, 0, response, 2, data.Length);
            return response;
        }

        if (functionCode == FunctionWriteSingleRegister)
        {
            var address = (pdu[1] << 8) | pdu[2];
            var value = (ushort)((pdu[3] << 8) | pdu[4]);
            _registers[address] = value;
            return pdu; // FC06 응답은 요청 그대로 에코
        }

        if (functionCode == FunctionWriteMultipleRegisters)
        {
            var start = (pdu[1] << 8) | pdu[2];
            var quantity = (pdu[3] << 8) | pdu[4];
            for (var i = 0; i < quantity; i++)
            {
                var value = (ushort)((pdu[6 + (i * 2)] << 8) | pdu[6 + (i * 2) + 1]);
                _registers[start + i] = value;
            }

            return new byte[] { functionCode, pdu[1], pdu[2], pdu[3], pdu[4] };
        }

        return new byte[] { (byte)(functionCode | ExceptionFlag), 0x01 }; // Illegal Function
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }

            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
