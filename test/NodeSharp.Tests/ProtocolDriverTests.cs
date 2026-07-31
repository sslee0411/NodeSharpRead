using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="IProtocolDriver"/>/<see cref="ProtocolDriverType"/>/<see cref="PlcConnectionConfig"/>
/// (CT-09, 02번 설계 문서 11번 탭 카드 8 — Phase 1 Contracts의 마지막 Step)에 대한 단위 테스트입니다.
/// 실제 Modbus 구현체(<c>ModbusDriver</c>)는 <c>PD-01a/b</c>에서 작성하므로, 여기서는 더미 드라이버로
/// 인터페이스 계약 자체를 검증합니다.
/// ★ v1.71 정정: <c>IProtocolDriver.Type</c>이 고정 Enum에서 <c>string</c> 식별자로 바뀌면서, LS산전
/// XGT 등 카탈로그에 없는 프로토콜도 <see cref="ProtocolDriverRegistry"/>로 동적 등록 가능해졌다 —
/// 이를 검증하는 테스트를 추가했다.
/// </summary>
public class ProtocolDriverTests
{
    /// <summary>테스트 전용 <see cref="IProtocolDriver"/> 스텁 — 실제 통신 없이 고정 바이트를 반환.</summary>
    private sealed class FakeProtocolDriver : IProtocolDriver
    {
        public string Type => ProtocolDriverType.ModbusTcp;
        public PlcConnectionConfig? LastConnectConfig { get; private set; }
        public (int Address, byte[] Data)? LastWrite { get; private set; }

        public Task ConnectAsync(PlcConnectionConfig config, CancellationToken ct)
        {
            LastConnectConfig = config;
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(int startAddress, int lengthBytes, CancellationToken ct) =>
            Task.FromResult(Enumerable.Repeat((byte)0xAB, lengthBytes).ToArray());

        public Task WriteAsync(int address, byte[] data, CancellationToken ct)
        {
            LastWrite = (address, data);
            return Task.CompletedTask;
        }
    }

    /// <summary>등록 테스트 전용 더미 드라이버 타입 — 실제로는 <c>LsXgtDriver</c> 같은 플러그인 dll의 타입을 대신함.</summary>
    private sealed class FakeLsXgtDriver : IProtocolDriver
    {
        public string Type => ProtocolDriverType.LsXgt;
        public Task ConnectAsync(PlcConnectionConfig config, CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]> ReadAsync(int startAddress, int lengthBytes, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task WriteAsync(int address, byte[] data, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task IProtocolDriver_ReadAsync는_요청한_길이만큼_바이트_배열을_반환한다()
    {
        IProtocolDriver driver = new FakeProtocolDriver();

        byte[] raw = await driver.ReadAsync(startAddress: 40001, lengthBytes: 4, CancellationToken.None);

        Assert.Equal(4, raw.Length);
    }

    [Fact]
    public async Task IProtocolDriver_ConnectAsync에_전달한_PlcConnectionConfig가_드라이버에_그대로_전달된다()
    {
        var driver = new FakeProtocolDriver();
        var config = new PlcConnectionConfig(Host: "192.168.1.10", Port: 502);

        await driver.ConnectAsync(config, CancellationToken.None);

        Assert.Equal(config, driver.LastConnectConfig);
    }

    [Fact]
    public async Task IProtocolDriver_WriteAsync_호출_인자가_그대로_기록된다()
    {
        var driver = new FakeProtocolDriver();
        var data = new byte[] { 1, 2, 3, 4 };

        await driver.WriteAsync(address: 40001, data, CancellationToken.None);

        Assert.Equal((40001, data), driver.LastWrite);
    }

    [Fact]
    public void ProtocolDriverType_상수는_모두_서로_다른_비어있지_않은_문자열이다()
    {
        var values = new[]
        {
            ProtocolDriverType.ModbusTcp,
            ProtocolDriverType.ModbusRtu,
            ProtocolDriverType.SiemensS7,
            ProtocolDriverType.LsXgt,
            ProtocolDriverType.MitsubishiA,
            ProtocolDriverType.MitsubishiQnA,
            ProtocolDriverType.CimonHd,
        };

        Assert.All(values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void PlcConnectionConfig_RTU_모드는_ComPort와_BaudRate를_사용한다()
    {
        var rtuConfig = new PlcConnectionConfig(Host: "", Port: 0, ComPort: "COM3", BaudRate: 19200);

        Assert.Equal("COM3", rtuConfig.ComPort);
        Assert.Equal(19200, rtuConfig.BaudRate);
    }

    [Fact]
    public void PlcConnectionConfig_TCP_모드는_ComPort_생략_시_null이고_BaudRate는_기본값_9600이다()
    {
        var tcpConfig = new PlcConnectionConfig(Host: "192.168.1.10", Port: 502);

        Assert.Null(tcpConfig.ComPort);
        Assert.Equal(9600, tcpConfig.BaudRate);
    }

    [Fact]
    public void ProtocolDriverRegistry는_카탈로그에_없는_새_프로토콜도_동적으로_등록할_수_있다()
    {
        var registry = new ProtocolDriverRegistry(contractsVersion: "1.0.0");
        var manifest = new ProtocolDriverManifest(ProtocolDriverType.LsXgt, DriverVersion: "1.0.0", RequiredContractsVersion: "1.0.0");

        bool ok = registry.TryRegister(manifest, typeof(FakeLsXgtDriver));

        Assert.True(ok);
        Assert.Equal(typeof(FakeLsXgtDriver), registry.RegisteredDrivers[ProtocolDriverType.LsXgt]);
    }

    [Fact]
    public void ProtocolDriverRegistry는_Contracts_주버전이_다르면_등록을_거부한다()
    {
        var registry = new ProtocolDriverRegistry(contractsVersion: "1.0.0");
        var manifest = new ProtocolDriverManifest(ProtocolDriverType.MitsubishiA, DriverVersion: "1.0.0", RequiredContractsVersion: "2.0.0");

        bool ok = registry.TryRegister(manifest, typeof(FakeLsXgtDriver));

        Assert.False(ok);
        Assert.False(registry.RegisteredDrivers.ContainsKey(ProtocolDriverType.MitsubishiA));
    }

    [Fact]
    public void ProtocolDriverRegistry는_한_프로토콜_등록_거부가_다른_프로토콜_등록을_막지_않는다()
    {
        var registry = new ProtocolDriverRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new ProtocolDriverManifest("Legacy.Old", "0.9.0", RequiredContractsVersion: "2.0.0"), typeof(object));

        bool ok = registry.TryRegister(
            new ProtocolDriverManifest(ProtocolDriverType.CimonHd, "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(FakeLsXgtDriver));

        Assert.True(ok);
        Assert.True(registry.RegisteredDrivers.ContainsKey(ProtocolDriverType.CimonHd));
        Assert.False(registry.RegisteredDrivers.ContainsKey("Legacy.Old"));
    }
}
