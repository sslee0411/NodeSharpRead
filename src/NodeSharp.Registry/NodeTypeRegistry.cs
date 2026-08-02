using System.Reflection;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Util;

namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 노드 타입 레지스트리
/// 역활 및 기능 : 플러그인 dll에서 발견한 노드 타입을 버전 호환성 검사와 함께 관리하는 레지스트리
///
/// <see cref="PluginLoader"/>가 로드한 플러그인 dll에서 발견한 노드 타입을 관리합니다.
/// 등록 전에 <see cref="SemVer.IsCompatible"/>로 플러그인이 요구하는 Contracts 버전과 현재 Contracts
/// 버전을 비교해, 불일치하면 크래시 대신 해당 플러그인만 제외하고 계속 진행합니다.
/// 설계 근거: 02번 문서 10번 탭 카드 8(플러그인 버전 호환성 검사). PluginLoadContext/PluginLoader와
/// 동일한 사유(v1.66)로 Contracts가 아니라 Registry 소속입니다.
/// (★ RT-01a 추가) <see cref="INodeRegistry"/>를 구현해 <c>FlowEngine</c>이 <see cref="CreateInstance"/>로
/// <see cref="NodeConfig.Type"/> 문자열만으로 노드 인스턴스를 만들 수 있게 합니다(2번 탭 카드 4).
/// (★ RG-01 추가) <see cref="ScanAssembly"/>로 <see cref="INodeTypeDescriptor"/>를 노출하는 타입을
/// 어셈블리에서 찾아 <see cref="Descriptors"/>에 수집하고, <see cref="CreateInstance"/>가 오래 미뤄뒀던
/// "<see cref="IFlowNode.Id"/>를 <see cref="NodeConfig.Id"/>와 동기화" 완료 기준을 이제 두 경로(신규
/// Descriptor 기반·기존 <see cref="TryRegister"/> Type 기반) 모두에서 만족합니다.
/// </summary>
/// <example>
/// <code>
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
///
/// // 1) 기존 방식(RT-01a) — Type만 등록, RG-01부터는 Id도 함께 동기화됨
/// bool ok = registry.TryRegister(
///     new PluginManifest("inject", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(InjectNode));
/// // ok == true, registry.RegisteredTypes["inject"] == typeof(InjectNode)
///
/// // 주 버전이 맞지 않는 플러그인 — 크래시 대신 조용히 거부
/// bool rejected = registry.TryRegister(
///     new PluginManifest("legacy", "0.9.0", RequiredContractsVersion: "2.0.0"), typeof(object));
/// // rejected == false, registry.RegisteredTypes에 "legacy" 없음
///
/// var cfg = new NodeConfig("n1", "inject", "타이머", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = registry.CreateInstance(cfg);   // typeof(InjectNode) 인스턴스, node.Id == "n1"
///
/// // 2) RG-01 방식 — 어셈블리를 스캔해 INodeTypeDescriptor(Descriptor 정적 필드 관례)를 한꺼번에 수집
/// int found = registry.ScanAssembly(typeof(HttpRequestNodeType).Assembly);
/// var httpCfg = new NodeConfig("n2", "http-request", "센서 조회", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode httpNode = registry.CreateInstance(httpCfg);   // Descriptor.Factory가 Id/Name 동기화
/// </code>
/// </example>
public sealed class NodeTypeRegistry : INodeRegistry
{
    private readonly Dictionary<string, Type> _types = new();
    private readonly Dictionary<string, INodeTypeDescriptor> _descriptors = new();
    private readonly string _contractsVersion;

    /// <summary>현재 로드된 <c>NodeSharp.Contracts</c> 어셈블리의 버전을 기준으로 레지스트리를 생성합니다.</summary>
    public NodeTypeRegistry(string contractsVersion) => _contractsVersion = contractsVersion;

    /// <summary>지금까지 등록에 성공한 노드 타입 목록입니다. Key는 <see cref="PluginManifest.TypeName"/>.</summary>
    public IReadOnlyDictionary<string, Type> RegisteredTypes => _types;

    /// <summary>
    /// (★ RG-01) <see cref="ScanAssembly"/>로 수집한 <see cref="INodeTypeDescriptor"/> 목록입니다.
    /// Key는 <see cref="INodeTypeDescriptor.TypeName"/>.
    /// </summary>
    public IReadOnlyDictionary<string, INodeTypeDescriptor> Descriptors => _descriptors;

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

    /// <summary>
    /// (★ RG-01) <paramref name="assembly"/>의 공개 타입을 훑어 <see cref="INodeTypeDescriptor"/> 타입의
    /// <c>public static</c> 필드/프로퍼티(관례적으로 이름은 <c>Descriptor</c>, 02번 문서 9번 탭 카드3
    /// <c>HttpRequestNodeType.Descriptor</c> 예시)를 찾아 <see cref="Descriptors"/>에 등록합니다. 같은
    /// <see cref="INodeTypeDescriptor.TypeName"/>이 이미 있으면 나중 것으로 덮어씁니다(재스캔해도 안전).
    /// </summary>
    /// <returns>새로 찾아 등록한 디스크립터 개수.</returns>
    public int ScanAssembly(Assembly assembly)
    {
        var count = 0;
        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (typeof(INodeTypeDescriptor).IsAssignableFrom(field.FieldType) &&
                    field.GetValue(null) is INodeTypeDescriptor fieldDescriptor)
                {
                    _descriptors[fieldDescriptor.TypeName] = fieldDescriptor;
                    count++;
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (typeof(INodeTypeDescriptor).IsAssignableFrom(property.PropertyType) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetValue(null) is INodeTypeDescriptor propertyDescriptor)
                {
                    _descriptors[propertyDescriptor.TypeName] = propertyDescriptor;
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// <paramref name="cfg"/>.Type에 해당하는 노드 인스턴스를 생성합니다. <see cref="Descriptors"/>에
    /// 등록된 타입이면(★ RG-01) <see cref="INodeTypeDescriptor.Factory"/>가 <see cref="IFlowNode.Id"/>/
    /// <see cref="IFlowNode.Name"/> 동기화까지 책임집니다. 없으면 기존 RT-01a 방식(<see cref="RegisteredTypes"/>,
    /// <c>Activator.CreateInstance</c>)으로 대체하되, ★ RG-01부터는 이 레거시 경로도
    /// <see cref="NodeIdBinder.Bind"/>로 <c>Id</c>를 함께 동기화합니다(오래 미뤄뒀던 완료 기준 — 상세
    /// 근거는 <see cref="NodeIdBinder"/> XML 주석 참고).
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cfg"/>.Type이 <see cref="Descriptors"/>·<see cref="RegisteredTypes"/> 어디에도 없을 때.</exception>
    public IFlowNode CreateInstance(NodeConfig cfg)
    {
        if (_descriptors.TryGetValue(cfg.Type, out var descriptor))
        {
            return descriptor.Factory(cfg);
        }

        if (!_types.TryGetValue(cfg.Type, out var nodeType))
        {
            throw new InvalidOperationException($"노드 타입 '{cfg.Type}'을(를) 찾을 수 없습니다.");
        }

        var node = (IFlowNode)Activator.CreateInstance(nodeType)!;
        NodeIdBinder.Bind(node, cfg.Id);
        node.Name = cfg.Name;
        return node;
    }
}
