using NodeSharp.Contracts.Models;
using NodeSharp.Util;

namespace NodeSharp.Registry;

/// <summary>
/// <see cref="PluginLoader"/>가 로드한 플러그인 dll에서 발견한 노드 타입을 관리합니다.
/// 등록 전에 <see cref="SemVer.IsCompatible"/>로 플러그인이 요구하는 Contracts 버전과 현재 Contracts
/// 버전을 비교해, 불일치하면 크래시 대신 해당 플러그인만 제외하고 계속 진행합니다.
/// 설계 근거: 02번 문서 10번 탭 카드 8(플러그인 버전 호환성 검사). PluginLoadContext/PluginLoader와
/// 동일한 사유(v1.66)로 Contracts가 아니라 Registry 소속입니다.
/// </summary>
/// <example>
/// <code>
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
///
/// // 호환되는 플러그인 — 등록 성공
/// bool ok = registry.TryRegister(
///     new PluginManifest("inject", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(InjectNode));
/// // ok == true, registry.RegisteredTypes["inject"] == typeof(InjectNode)
///
/// // 주 버전이 맞지 않는 플러그인 — 크래시 대신 조용히 거부
/// bool rejected = registry.TryRegister(
///     new PluginManifest("legacy", "0.9.0", RequiredContractsVersion: "2.0.0"), typeof(object));
/// // rejected == false, registry.RegisteredTypes에 "legacy" 없음
/// </code>
/// </example>
public sealed class NodeTypeRegistry
{
    private readonly Dictionary<string, Type> _types = new();
    private readonly string _contractsVersion;

    /// <summary>현재 로드된 <c>NodeSharp.Contracts</c> 어셈블리의 버전을 기준으로 레지스트리를 생성합니다.</summary>
    public NodeTypeRegistry(string contractsVersion) => _contractsVersion = contractsVersion;

    /// <summary>지금까지 등록에 성공한 노드 타입 목록입니다. Key는 <see cref="PluginManifest.TypeName"/>.</summary>
    public IReadOnlyDictionary<string, Type> RegisteredTypes => _types;

    /// <summary>
    /// <paramref name="manifest"/>가 요구하는 Contracts 버전이 현재 버전과 호환되면
    /// <paramref name="nodeType"/>을 등록하고 <c>true</c>를 반환합니다. 호환되지 않으면 등록하지 않고
    /// <c>false</c>를 반환합니다(호출 측이 경고 로그를 남기고 다음 플러그인으로 계속 진행).
    /// </summary>
    public bool TryRegister(PluginManifest manifest, Type nodeType)
    {
        if (!SemVer.IsCompatible(manifest.RequiredContractsVersion, _contractsVersion))
            return false;

        _types[manifest.TypeName] = nodeType;
        return true;
    }
}
