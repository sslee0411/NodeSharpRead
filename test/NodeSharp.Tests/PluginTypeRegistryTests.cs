using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Util;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="PluginManifest"/>(Contracts)/<see cref="SemVer"/>(Util)/<see cref="NodeTypeRegistry"/>
/// (Registry)에 대한 단위 테스트입니다(CT-06b, 02번 설계 문서 10번 탭 카드 8). 완료 기준이 요구하는
/// "Contracts 버전을 인위적으로 불일치시키면 SemVer 가드가 로드를 거부하는지"를 직접 검증합니다.
/// </summary>
public class PluginTypeRegistryTests
{
    [Theory]
    [InlineData("1.2.0", "1.0.0", true)]    // 주 버전 동일 → 호환
    [InlineData("1.0.0", "1.9.3", true)]    // 주 버전 동일(요구 버전이 더 낮아도 호환)
    [InlineData("2.0.0", "1.5.0", false)]   // 주 버전 불일치 → 비호환
    [InlineData("1.0", "1.0.0", true)]      // 생략된 자리는 0으로 간주
    public void SemVer_IsCompatible은_주_버전이_같을_때만_true를_반환한다(string required, string actual, bool expected)
    {
        Assert.Equal(expected, SemVer.IsCompatible(required, actual));
    }

    [Fact]
    public void SemVer_IsCompatible은_파싱할_수_없는_버전이면_false를_반환한다()
    {
        Assert.False(SemVer.IsCompatible("not-a-version", "1.0.0"));
    }

    [Fact]
    public void NodeTypeRegistry_호환되는_매니페스트는_등록에_성공한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var manifest = new PluginManifest(TypeName: "inject", PluginVersion: "1.0.0", RequiredContractsVersion: "1.0.0");

        bool ok = registry.TryRegister(manifest, typeof(object));

        Assert.True(ok);
        Assert.Equal(typeof(object), registry.RegisteredTypes["inject"]);
    }

    [Fact]
    public void NodeTypeRegistry_Contracts_주_버전이_불일치하면_로드를_거부한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var manifest = new PluginManifest(TypeName: "legacy", PluginVersion: "0.9.0", RequiredContractsVersion: "2.0.0");

        bool ok = registry.TryRegister(manifest, typeof(object));

        Assert.False(ok);
        Assert.False(registry.RegisteredTypes.ContainsKey("legacy"));
    }

    [Fact]
    public void NodeTypeRegistry_거부된_플러그인_하나가_이후_등록을_막지_않는다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("legacy", "0.9.0", "2.0.0"), typeof(object));   // 거부됨

        bool ok = registry.TryRegister(new PluginManifest("inject", "1.0.0", "1.0.0"), typeof(string));   // 계속 진행

        Assert.True(ok);
        Assert.Single(registry.RegisteredTypes);
    }
}
