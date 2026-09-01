using NodeSharp.Contracts.Models;
using NodeSharp.Drivers.Modbus;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (PD-01b) <see cref="ModbusDriver"/>가 RT-10 <see cref="SharedResourceManager"/>로 공유 관리될 때,
/// 같은 PLC(같은 <c>SharedResourceManager</c> id)를 참조하는 TagNode 여러 개가 배포돼도 실제
/// <see cref="ModbusDriver"/> 인스턴스는 1개만 생성/연결되는지 검증합니다(03번 Step맵 PD-01b 완료
/// 기준의 두 번째 항목). <see cref="SharedResourceManagerTests"/>·<see cref="RedeployVsSharedResourceTests"/>와
/// 동일한 선례 — LL-05b <c>NetIoNode</c>가 아직 없어(⏳ 대기) 실제 <c>PlcTagReadNode</c>/<c>PlcTagWriteNode</c>를
/// 통하지 않고, TagNode 배포 자리에 <see cref="SharedResourceManager.AcquireAsync{T}"/>를 직접 호출해
/// 증명합니다. TCP 모드로 검증합니다(PD-01a <c>FakeModbusTcpSlave</c> 재사용) — 공유 관리 로직 자체는
/// TCP/RTU 어느 모드든 <see cref="ModbusDriver.StartAsync"/>가 동일하게 <see cref="ModbusDriver.ConnectAsync"/>를
/// 호출하는 것으로 동작하므로, RTU 프레이밍 자체는 <c>ModbusDriverRtuTests</c>에서 이미 별도로 검증했습니다.
/// </summary>
public class ModbusDriverSharedResourceTests
{
    [Fact]
    public async Task 같은_PLC를_참조하는_TagNode_2개가_배포돼도_ModbusDriver_인스턴스는_1개만_생성된다()
    {
        await using var slave = new ModbusDriverTests.FakeModbusTcpSlave();
        slave.SetRegister(0, 0x2A);
        slave.Start();

        var manager = new SharedResourceManager();
        var config = new PlcConnectionConfig(Host: "127.0.0.1", Port: slave.Port);
        var factoryCallCount = 0;

        ModbusDriver Factory()
        {
            factoryCallCount++;
            return new ModbusDriver(id: "plc-1", config: config);
        }

        // TagNode #1 배포 — 최초 참조라 factory 실행 + StartAsync(=ConnectAsync)로 실제 연결됨
        var driverForTag1 = await manager.AcquireAsync("plc-1", Factory, CancellationToken.None);
        // TagNode #2 배포 — 같은 id라 이미 연결된 인스턴스를 그대로 재사용, factory는 다시 호출되지 않음
        var driverForTag2 = await manager.AcquireAsync("plc-1", Factory, CancellationToken.None);

        Assert.Same(driverForTag1, driverForTag2);
        Assert.Equal(1, factoryCallCount);

        // 참조만 같은 게 아니라 실제로 연결된 드라이버인지, 공유된 인스턴스로 통신까지 확인
        var raw = await driverForTag1.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None);
        Assert.Equal(new byte[] { 0x00, 0x2A }, raw);

        // TagNode #1 종료 — 참조 2 → 1, TagNode #2가 아직 참조 중이라 연결은 끊기지 않음
        await manager.ReleaseAsync("plc-1");
        var stillWorks = await driverForTag2.ReadAsync(startAddress: 0, lengthBytes: 2, CancellationToken.None);
        Assert.Equal(new byte[] { 0x00, 0x2A }, stillWorks);

        // TagNode #2 종료 — 마지막 참조 해제, 실제 연결까지 정리(StopAsync → Disconnect)됨
        await manager.ReleaseAsync("plc-1");
    }

    [Fact]
    public async Task StartAsync는_Config가_없으면_InvalidOperationException을_던진다()
    {
        // SharedResourceManager를 거치는 사용법에서 Config를 빠뜨리는 실수를 조기에 드러내기 위한 가드.
        var driver = new ModbusDriver(id: "plc-no-config");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 서로_다른_PLC를_참조하는_TagNode는_각자_별도의_ModbusDriver_인스턴스를_받는다()
    {
        await using var slaveA = new ModbusDriverTests.FakeModbusTcpSlave();
        slaveA.Start();
        await using var slaveB = new ModbusDriverTests.FakeModbusTcpSlave();
        slaveB.Start();

        var manager = new SharedResourceManager();
        var configA = new PlcConnectionConfig(Host: "127.0.0.1", Port: slaveA.Port);
        var configB = new PlcConnectionConfig(Host: "127.0.0.1", Port: slaveB.Port);

        var driverA = await manager.AcquireAsync("plc-a", () => new ModbusDriver(id: "plc-a", config: configA), CancellationToken.None);
        var driverB = await manager.AcquireAsync("plc-b", () => new ModbusDriver(id: "plc-b", config: configB), CancellationToken.None);

        Assert.NotSame(driverA, driverB);

        await manager.ReleaseAsync("plc-a");
        await manager.ReleaseAsync("plc-b");
    }
}
