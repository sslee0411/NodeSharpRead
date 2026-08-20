using System.Net;
using System.Net.Sockets;
using NodeSharp.Contracts.Models;
using NodeSharp.Drivers.Modbus;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ModbusDriver"/>(PD-01a, 02번 설계문서 11번 탭 카드8 1차 구현체)에 대한 단위 테스트입니다.
/// 실제 PLC 하드웨어 없이 완료 기준("실제(또는 시뮬레이터) Modbus TCP 슬레이브에 연결해 ReadAsync로
/// 값을 읽고, WriteAsync로 쓴 값이 다시 읽었을 때 반영되는지 확인")을 증명하기 위해, 이 파일 안에
/// <see cref="FakeModbusTcpSlave"/>(로컬 루프백 TcpListener + 메모리 레지스터 배열)를 직접 구현해
/// FC03/06/16 요청에 실제로 응답하는 슬레이브 역할을 합니다.
/// </summary>
public class ModbusDriverTests
{
    [Fact]
    public async Task ReadAsync는_슬레이브에_미리_채워둔_레지스터_값을_그대로_반환한다()
    {
        await using var slave = new FakeModbusTcpSlave();
        slave.SetRegister(0, 0x1234);
        slave.SetRegister(1, 0x5678);
        slave.Start();

        using var driver = new ModbusDriver();
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port), CancellationToken.None);

        var raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 4, CancellationToken.None);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, raw);
    }

    [Fact]
    public async Task WriteAsync_단일_레지스터_FC06_이후_ReadAsync가_변경된_값을_반환한다()
    {
        await using var slave = new FakeModbusTcpSlave();
        slave.Start();

        using var driver = new ModbusDriver();
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port), CancellationToken.None);

        await driver.WriteAsync(address: 3, data: new byte[] { 0xAB, 0xCD }, CancellationToken.None);
        var raw = await driver.ReadAsync(startAddress: 3, lengthBytes: 2, CancellationToken.None);

        Assert.Equal(new byte[] { 0xAB, 0xCD }, raw);
        Assert.Equal((ushort)0xABCD, slave.GetRegister(3));
    }

    [Fact]
    public async Task WriteAsync_다중_레지스터_FC16_이후_ReadAsync가_변경된_값을_모두_반환한다()
    {
        await using var slave = new FakeModbusTcpSlave();
        slave.Start();

        using var driver = new ModbusDriver();
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port), CancellationToken.None);

        var written = new byte[] { 0x00, 0x0A, 0x00, 0x0B, 0x00, 0x0C };
        await driver.WriteAsync(address: 10, data: written, CancellationToken.None);
        var raw = await driver.ReadAsync(startAddress: 10, lengthBytes: 6, CancellationToken.None);

        Assert.Equal(written, raw);
    }

    [Fact]
    public async Task ReadAsync는_ConnectAsync_호출_전에는_InvalidOperationException을_던진다()
    {
        using var driver = new ModbusDriver();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync는_lengthBytes가_홀수면_ArgumentException을_던진다()
    {
        await using var slave = new FakeModbusTcpSlave();
        slave.Start();

        using var driver = new ModbusDriver();
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(
            () => driver.ReadAsync(startAddress: 0, lengthBytes: 3, CancellationToken.None));
    }

    [Fact]
    public async Task 슬레이브가_예외_응답을_반환하면_ModbusException이_발생하고_예외_코드가_담긴다()
    {
        await using var slave = new FakeModbusTcpSlave();
        slave.RejectAllReads = true;
        slave.Start();

        using var driver = new ModbusDriver();
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ModbusException>(
            () => driver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None));

        Assert.Equal((byte?)0x02, ex.ExceptionCode);
    }

    [Fact]
    public async Task ConnectAsync는_RTU_모드에서_NotSupportedException을_던진다()
    {
        using var driver = new ModbusDriver(isRtu: true);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM3"), CancellationToken.None));
    }

    /// <summary>
    /// 테스트 전용 가짜 Modbus TCP 슬레이브 — 로컬 루프백(127.0.0.1)의 임의 포트에서 1개 연결을
    /// 받아 FC03(Read Holding Registers)/FC06(Write Single Register)/FC16(Write Multiple Registers)
    /// 요청에 실제로 MBAP 프레이밍 응답을 돌려줍니다. <see cref="ModbusDriver"/>가 실제 PLC
    /// 하드웨어 없이도 완료 기준(연결→읽기→쓰기→재읽기 왕복)을 증명할 수 있게 하는 목적입니다.
    /// </summary>
    private sealed class FakeModbusTcpSlave : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly ushort[] _registers = new ushort[64];
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptTask;

        public int Port { get; }

        /// <summary>true면 FC03 요청마다 Illegal Data Address(0x02) 예외 응답을 돌려줍니다(예외 처리 경로 테스트용).</summary>
        public bool RejectAllReads { get; set; }

        public FakeModbusTcpSlave()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public void SetRegister(int address, ushort value) => _registers[address] = value;

        public ushort GetRegister(int address) => _registers[address];

        /// <summary>연결 수락+요청 처리 루프를 백그라운드로 시작합니다. 테스트가 <see cref="ModbusDriver.ConnectAsync"/>를 호출하기 전에 먼저 불러야 합니다.</summary>
        public void Start() => _acceptTask = AcceptLoopAsync(_cts.Token);

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                using var stream = client.GetStream();

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
                    responseHeader[1] = header[1]; // Transaction Id를 그대로 에코
                    responseHeader[4] = (byte)(responseBody.Length >> 8);
                    responseHeader[5] = (byte)responseBody.Length;

                    await stream.WriteAsync(responseHeader, ct).ConfigureAwait(false);
                    await stream.WriteAsync(responseBody, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 테스트 종료 시 DisposeAsync가 취소 — 정상 종료 경로.
            }
            catch (IOException)
            {
                // 테스트가 driver를 먼저 Dispose해 소켓이 끊긴 경우 — 정상 종료 경로.
            }
            catch (ObjectDisposedException)
            {
                // _listener.Stop() 이후 AcceptTcpClientAsync가 던질 수 있음 — 정상 종료 경로.
            }
        }

        private byte[] HandlePdu(byte[] pdu)
        {
            var functionCode = pdu[0];

            if (functionCode == 0x03)
            {
                if (RejectAllReads)
                {
                    return new byte[] { (byte)(functionCode | 0x80), 0x02 }; // Illegal Data Address
                }

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

            if (functionCode == 0x06)
            {
                var address = (pdu[1] << 8) | pdu[2];
                var value = (ushort)((pdu[3] << 8) | pdu[4]);
                _registers[address] = value;
                return pdu; // FC06 응답은 요청 그대로 에코
            }

            if (functionCode == 0x10)
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

            return new byte[] { (byte)(functionCode | 0x80), 0x01 }; // Illegal Function
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
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

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptTask is not null)
            {
                try
                {
                    await _acceptTask.ConfigureAwait(false);
                }
                catch
                {
                    // 종료 경로 예외는 테스트 실패로 이어질 필요 없음 — 위 catch 블록들이 이미 흡수함.
                }
            }

            _cts.Dispose();
        }
    }
}
