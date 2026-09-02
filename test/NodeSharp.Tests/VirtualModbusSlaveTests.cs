using NodeSharp.Contracts.Models;
using NodeSharp.Drivers.Modbus;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (PD-01c) <see cref="VirtualModbusSlave"/>와 <see cref="ModbusDriver.VirtualSlave"/> 연동에 대한
/// 단위 테스트입니다. 완료 기준(사용자 확인 "핵심만 우선 구현", 2026-09-01 — 03번 Step맵 PD-01c):
/// "ModbusDriver(PD-01a/b) 입장에서는 실제 Tcp/Serial과 구분되지 않게 동작"을 증명하기 위해, 실제
/// TCP 소켓/COM 포트 없이 <see cref="ModbusDriver.ReadAsync"/>/<see cref="ModbusDriver.WriteAsync"/>가
/// <see cref="VirtualModbusSlave"/>를 상대로 정상 동작하는지, 그리고 RTU 모드와 함께 쓰면 명시된
/// <see cref="ArgumentException"/> 가드가 동작하는지 확인합니다.
/// </summary>
public class VirtualModbusSlaveTests
{
    [Fact]
    public async Task ReadAsync는_VirtualSlave에_미리_채워둔_레지스터_값을_그대로_반환한다()
    {
        var slave = new VirtualModbusSlave();
        slave.SetRegister(0, 0x1234);
        slave.SetRegister(1, 0x5678);

        using var driver = new ModbusDriver { VirtualSlave = slave };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);

        var raw = await driver.ReadAsync(startAddress: 0, lengthBytes: 4, CancellationToken.None);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, raw);
    }

    [Fact]
    public async Task WriteAsync_단일_레지스터_이후_ReadAsync가_변경된_값을_반환하고_VirtualSlave에도_즉시_반영된다()
    {
        var slave = new VirtualModbusSlave();

        using var driver = new ModbusDriver { VirtualSlave = slave };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);

        await driver.WriteAsync(address: 3, data: new byte[] { 0xAB, 0xCD }, CancellationToken.None);
        var raw = await driver.ReadAsync(startAddress: 3, lengthBytes: 2, CancellationToken.None);

        Assert.Equal(new byte[] { 0xAB, 0xCD }, raw);
        Assert.Equal((ushort)0xABCD, slave.GetRegister(3));
    }

    [Fact]
    public async Task WriteAsync_다중_레지스터도_VirtualSlave에_정상_반영된다()
    {
        // FC16(0x10, Write Multiple Registers) 경로 — data.Length > 2일 때 ModbusDriver.WriteAsync가 선택.
        var slave = new VirtualModbusSlave();

        using var driver = new ModbusDriver { VirtualSlave = slave };
        await driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);

        await driver.WriteAsync(address: 10, data: new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 }, CancellationToken.None);
        var raw = await driver.ReadAsync(startAddress: 10, lengthBytes: 6, CancellationToken.None);

        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 }, raw);
    }

    [Fact]
    public async Task ConnectAsync는_VirtualSlave와_isRtu가_함께_설정되면_ArgumentException을_던진다()
    {
        // VirtualModbusSlave는 MBAP(TCP) 프레이밍만 지원 — ModbusDriver.cs ConnectAsync 문서/클래스 remarks 참고.
        var slave = new VirtualModbusSlave();

        using var driver = new ModbusDriver(isRtu: true) { VirtualSlave = slave };

        await Assert.ThrowsAsync<ArgumentException>(
            () => driver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM3"), CancellationToken.None));
    }

    [Fact]
    public async Task 같은_VirtualSlave에_두_번째로_Connect하면_새_연결이_정상적으로_통신한다()
    {
        // VirtualModbusSlave.Connect() remarks의 "동시 연결 1개" 설계를 ModbusDriver 두 인스턴스로 확인합니다.
        // (주의) 이전 연결(firstDriver)에 대고 ReadAsync를 다시 호출하면 응답 상대(RunAsync 루프)가 이미
        // 끊겨 있어 CancellationToken.None으로는 영원히 대기할 수 있으므로, 이 테스트는 새 연결
        // (secondDriver)이 정상 동작하는지 + firstDriver를 Dispose()해도(응답을 기다리지 않는 한) 안전한지만
        // 확인합니다 — "요청 중 하나가 응답 없이 걸려 있으면 예외로 끝난다"는 별도 성격의 검증이라
        // 여기서 함께 증명하지 않습니다.
        var slave = new VirtualModbusSlave();
        slave.SetRegister(0, 0x2A);

        var firstDriver = new ModbusDriver { VirtualSlave = slave };
        await firstDriver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);

        using var secondDriver = new ModbusDriver { VirtualSlave = slave };
        await secondDriver.ConnectAsync(new PlcConnectionConfig(Host: "", Port: 0), CancellationToken.None);

        var raw = await secondDriver.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None);
        Assert.Equal(new byte[] { 0x00, 0x2A }, raw);

        firstDriver.Dispose(); // 응답을 기다리지 않는 Dispose는 안전해야 한다(스트림 정리만 수행).
    }
}
