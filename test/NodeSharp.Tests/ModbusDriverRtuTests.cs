using NodeSharp.Contracts.Models;
using NodeSharp.Drivers.Modbus;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (PD-01b) <see cref="ModbusDriver"/>의 RTU 모드(<see cref="System.IO.Ports.SerialPort"/> + CRC16)에
/// 대한 단위 테스트입니다. 완료 기준(03번 Step맵 PD-01b): "RTU 모드에서 CRC 오류 응답은 거부되고
/// 정상 응답만 반영되는지"를 실제 COM 포트 없이 증명하기 위해, 이 파일 안에 <see cref="FakeModbusRtuSlave"/>
/// (그 위에서 실제 RTU ADU+CRC16 프레이밍으로 응답하는 가짜 슬레이브)를 직접 구현합니다 — TCP 쪽
/// <c>ModbusDriverTests</c>의 <c>FakeModbusTcpSlave</c>가 실제 루프백 소켓을 쓰는 것과 동일한 취지로,
/// 목(mock)이 아니라 실제 바이트 왕복 경로를 태웁니다. 양방향 Stream 페어는 (PD-01c, ★ 변경) 이 파일
/// 전용 <c>DuplexPairStream</c>을 별도로 두지 않고 프로덕션으로 승격된
/// <see cref="NodeSharp.Drivers.Modbus.InMemoryDuplexStream"/>(<c>InternalsVisibleTo</c>로 이 테스트
/// 프로젝트에 공개됨, <see cref="VirtualModbusSlave"/>가 쓰는 것과 동일 클래스)를 재사용합니다.
/// <see cref="ModbusDriver.RtuStreamFactory"/>(internal, PD-01b에서 신설)로 실제 SerialPort 대신 이
/// 페어를 주입합니다.
/// </summary>
public class ModbusDriverRtuTests
{
    [Fact]
    public async Task RTU_ReadAsync는_슬레이브에_미리_채워둔_레지스터_값을_그대로_반환한다()
    {
        var (masterStream, slaveStream) = InMemoryDuplexStream.CreatePair();
        var slave = new FakeModbusRtuSlave(slaveStream);
        slave.SetRegister(0, 0x1234);
        slave.SetRegister(1, 0x5678);
        var slaveTask = slave.RunAsync();

        using var driver = new ModbusDriver(isRtu: true) { RtuStreamFactory = (_, _) => Task.FromResult<Stream>(masterStream) };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM_TEST"), CancellationToken.None);

        var raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 4, CancellationToken.None);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, raw);
        slave.Stop();
        await slaveTask;
    }

    [Fact]
    public async Task RTU_WriteAsync_단일_레지스터_이후_ReadAsync가_변경된_값을_반환한다()
    {
        var (masterStream, slaveStream) = InMemoryDuplexStream.CreatePair();
        var slave = new FakeModbusRtuSlave(slaveStream);
        var slaveTask = slave.RunAsync();

        using var driver = new ModbusDriver(isRtu: true) { RtuStreamFactory = (_, _) => Task.FromResult<Stream>(masterStream) };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM_TEST"), CancellationToken.None);

        await driver.WriteAsync(address: 3, data: new byte[] { 0xAB, 0xCD }, CancellationToken.None);
        var raw = await driver.ReadAsync(startAddress: 3, lengthBytes: 2, CancellationToken.None);

        Assert.Equal(new byte[] { 0xAB, 0xCD }, raw);
        Assert.Equal((ushort)0xABCD, slave.GetRegister(3));
        slave.Stop();
        await slaveTask;
    }

    [Fact]
    public async Task RTU_CRC가_손상된_응답은_거부되고_ModbusException이_발생한다()
    {
        // 완료 기준의 핵심 항목: CRC 오류 응답은 정상 값으로 반영되지 않고 반드시 예외로 거부돼야 한다.
        var (masterStream, slaveStream) = InMemoryDuplexStream.CreatePair();
        var slave = new FakeModbusRtuSlave(slaveStream) { CorruptNextResponseCrc = true };
        slave.SetRegister(0, 0x1234);
        var slaveTask = slave.RunAsync();

        using var driver = new ModbusDriver(isRtu: true) { RtuStreamFactory = (_, _) => Task.FromResult<Stream>(masterStream) };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM_TEST"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ModbusException>(
            () => driver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None));

        // 프레이밍/CRC 오류는 슬레이브가 보낸 정식 예외 코드가 아니므로 ExceptionCode는 null이어야 한다
        // (ModbusDriver.cs SendAndReceiveRtuAsync 문서 참고).
        Assert.Null(ex.ExceptionCode);

        slave.Stop();
        await slaveTask;
    }

    [Fact]
    public async Task RTU_슬레이브가_예외_응답을_반환하면_CRC가_정상이어도_ModbusException이_발생하고_예외_코드가_담긴다()
    {
        var (masterStream, slaveStream) = InMemoryDuplexStream.CreatePair();
        var slave = new FakeModbusRtuSlave(slaveStream) { RejectAllReads = true };
        var slaveTask = slave.RunAsync();

        using var driver = new ModbusDriver(isRtu: true) { RtuStreamFactory = (_, _) => Task.FromResult<Stream>(masterStream) };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM_TEST"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ModbusException>(
            () => driver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None));

        Assert.Equal((byte?)0x02, ex.ExceptionCode);

        slave.Stop();
        await slaveTask;
    }

    /// <summary>
    /// (PD-01b) 테스트 전용 가짜 Modbus RTU 슬레이브 — <see cref="NodeSharp.Drivers.Modbus.InMemoryDuplexStream"/>의 Slave 쪽에서
    /// [슬레이브주소(1)][함수코드(1)][데이터][CRC16(2)] 형식의 요청을 읽어 FC03/FC06 요청에 실제로
    /// CRC16이 붙은 RTU 프레이밍 응답을 돌려줍니다. <see cref="CorruptNextResponseCrc"/>를 켜면 다음
    /// 응답 1건의 CRC 바이트를 일부러 훼손해 <see cref="ModbusDriver"/>의 CRC 거부 경로를 검증할 수
    /// 있습니다.
    /// </summary>
    private sealed class FakeModbusRtuSlave
    {
        private readonly Stream _stream;
        private readonly ushort[] _registers = new ushort[64];
        private readonly CancellationTokenSource _cts = new();

        public FakeModbusRtuSlave(Stream stream) => _stream = stream;

        /// <summary>true면 FC03 요청마다 Illegal Data Address(0x02) 예외 응답을 돌려줍니다(예외 처리 경로 테스트용).</summary>
        public bool RejectAllReads { get; set; }

        /// <summary>true면 다음 응답 1건만 CRC 하위 바이트를 뒤집어 보냅니다(CRC 거부 경로 테스트용) — 응답 1건을 보내고 나면 자동으로 false로 되돌아갑니다.</summary>
        public bool CorruptNextResponseCrc { get; set; }

        public void SetRegister(int address, ushort value) => _registers[address] = value;

        public ushort GetRegister(int address) => _registers[address];

        public void Stop() => _cts.Cancel();

        /// <summary>연결 하나짜리 요청/응답 루프를 백그라운드로 돌립니다 — 테스트가 <see cref="Stop"/>을 부르거나 스트림이 끊기면 종료합니다.</summary>
        public async Task RunAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var header = new byte[2]; // [슬레이브 주소][함수 코드]
                    if (!await ReadExactAsync(header, _cts.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var functionCode = header[1];
                    byte[] body;
                    if (functionCode == 0x03)
                    {
                        body = new byte[4]; // 시작주소(2)+수량(2)
                    }
                    else if (functionCode == 0x06)
                    {
                        body = new byte[4]; // 주소(2)+값(2)
                    }
                    else
                    {
                        return; // 이 테스트 슬레이브는 FC03/06만 처리
                    }

                    if (!await ReadExactAsync(body, _cts.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var crcBuf = new byte[2];
                    if (!await ReadExactAsync(crcBuf, _cts.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var response = BuildResponse(functionCode, body);
                    await SendFrameAsync(response, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 테스트 종료(Stop) 경로 — 정상 종료.
            }
        }

        private byte[] BuildResponse(byte functionCode, byte[] body)
        {
            if (RejectAllReads && functionCode == 0x03)
            {
                return new byte[] { (byte)(functionCode | 0x80), 0x02 }; // [함수코드|0x80][예외코드]
            }

            if (functionCode == 0x03)
            {
                var start = (body[0] << 8) | body[1];
                var quantity = (body[2] << 8) | body[3];
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

            // FC06 — 요청받은 주소에 값을 반영하고, 요청 그대로 에코
            var address = (body[0] << 8) | body[1];
            var value16 = (ushort)((body[2] << 8) | body[3]);
            _registers[address] = value16;
            return new byte[] { functionCode, body[0], body[1], body[2], body[3] };
        }

        /// <summary>[슬레이브주소]+<paramref name="pdu"/>에 CRC16을 붙여 전송합니다. <see cref="CorruptNextResponseCrc"/>가 켜져 있으면 CRC 하위 바이트를 뒤집어 보내고 즉시 꺼집니다.</summary>
        private async Task SendFrameAsync(byte[] pdu, CancellationToken ct)
        {
            var frame = new byte[1 + pdu.Length + 2];
            frame[0] = 1; // 이 테스트 슬레이브의 고정 주소(ModbusDriver 기본 unitId=1과 일치)
            Array.Copy(pdu, 0, frame, 1, pdu.Length);
            var crc = ComputeCrc16(frame.AsSpan(0, frame.Length - 2));
            var lowByte = (byte)crc;
            var highByte = (byte)(crc >> 8);

            if (CorruptNextResponseCrc)
            {
                lowByte ^= 0xFF; // CRC를 일부러 훼손 — 반드시 계산값과 달라짐
                CorruptNextResponseCrc = false;
            }

            frame[^2] = lowByte;
            frame[^1] = highByte;

            await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
        }

        private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        /// <summary>ModbusDriver.ComputeCrc16과 동일한 Modbus RTU 표준 CRC16(다항식 0xA001, 초기값 0xFFFF) — 슬레이브 쪽에서도 독립적으로 계산해야 하므로 여기서도 별도 구현합니다.</summary>
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
    }
}
