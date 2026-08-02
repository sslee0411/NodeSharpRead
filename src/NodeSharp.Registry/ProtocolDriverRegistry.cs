using NodeSharp.Contracts.Models;
using NodeSharp.Util;

namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 프로토콜 드라이버 레지스트리
/// 역활 및 기능 : 런타임에 등록된 IProtocolDriver 구현체를 관리하는 레지스트리
///
/// 런타임에 등록된 <c>IProtocolDriver</c> 구현체(문자열 프로토콜 식별자 → 드라이버 <see cref="Type"/>)를
/// 관리합니다. <see cref="NodeTypeRegistry"/>(CT-06b, <c>IFlowNode</c> 플러그인용)와 정확히 동일한 구조를
/// 프로토콜 드라이버 축에 적용한 것입니다 — 노드 팔레트 확장과 PLC 프로토콜 확장은 서로 다른 관심사
/// (<c>IFlowNode</c> vs <c>IProtocolDriver</c>)라 레지스트리도 별도로 둡니다.
/// 설계 근거: 02번 문서 11번 탭 카드 8(★ v1.71 정정 — LS산전 XGT·미쯔비시 A/QnA·CIMON HD 등 새 PLC
/// 프로토콜을 Contracts 재컴파일 없이 추가할 수 있도록 동적 등록 구조 도입). 드라이버 dll 자체의
/// 로드/언로드는 <see cref="PluginLoadContext"/>/<see cref="PluginLoader"/>(CT-06a)를 그대로 재사용하고,
/// 이 클래스는 로드된 어셈블리에서 찾은 드라이버 타입을 문자열 식별자로 매핑·버전 검사만 담당합니다.
/// </summary>
/// <example>
/// <code>
/// var registry = new ProtocolDriverRegistry(contractsVersion: "1.0.0");
///
/// // 호환되는 드라이버 플러그인 — 등록 성공, Contracts 코드 수정 없이 새 프로토콜 추가
/// bool ok = registry.TryRegister(
///     new ProtocolDriverManifest(ProtocolDriverType.LsXgt, "1.0.0", RequiredContractsVersion: "1.0.0"),
///     typeof(LsXgtDriver));
/// // ok == true, registry.RegisteredDrivers["LS.XGT"] == typeof(LsXgtDriver)
///
/// // 주 버전이 맞지 않는 드라이버 — 크래시 대신 조용히 거부
/// bool rejected = registry.TryRegister(
///     new ProtocolDriverManifest("Legacy.Old", "0.9.0", RequiredContractsVersion: "2.0.0"), typeof(object));
/// // rejected == false, registry.RegisteredDrivers에 "Legacy.Old" 없음
/// </code>
/// </example>
public sealed class ProtocolDriverRegistry
{
    private readonly Dictionary<string, Type> _registeredDrivers = new();
    private readonly string _contractsVersion;

    /// <summary>현재 로드된 <c>NodeSharp.Contracts</c> 어셈블리의 버전을 기준으로 레지스트리를 생성합니다.</summary>
    public ProtocolDriverRegistry(string contractsVersion) => _contractsVersion = contractsVersion;

    /// <summary>지금까지 등록에 성공한 드라이버 목록입니다. Key는 <see cref="ProtocolDriverManifest.ProtocolTypeName"/>.</summary>
    public IReadOnlyDictionary<string, Type> RegisteredDrivers => _registeredDrivers;

    /// <summary>
    /// <paramref name="manifest"/>가 요구하는 Contracts 버전이 현재 버전과 호환되면
    /// <paramref name="driverType"/>을 등록하고 <c>true</c>를 반환합니다. 호환되지 않으면 등록하지 않고
    /// <c>false</c>를 반환합니다(호출 측이 경고 로그를 남기고 다음 드라이버로 계속 진행).
    /// </summary>
    public bool TryRegister(ProtocolDriverManifest manifest, Type driverType)
    {
        if (!SemVer.IsCompatible(manifest.RequiredContractsVersion, _contractsVersion))
            return false;

        _registeredDrivers[manifest.ProtocolTypeName] = driverType;
        return true;
    }
}
