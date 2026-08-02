using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 노드 타입 메타데이터 계약
/// 역활 및 기능 : 노드 팔레트 표시·인스턴스 생성에 필요한 노드 타입 메타데이터(이름/분류/속성 스키마/팩토리)를 담는 계약
///
/// 노드 타입 하나(예: <c>"function"</c>, <c>"http-request"</c>)를 팔레트에 표시하고 실제 인스턴스를
/// 만드는 데 필요한 메타데이터를 담는 계약입니다(RG-01, Node-RED의 <c>&lt;node&gt;.html</c> 등록 정보에
/// 대응). 02번 문서 2번 탭 카드1(<c>TypeName</c>/<c>Category</c>/<c>IconGlyph</c>/<c>DefaultInputs</c>/
/// <c>DefaultOutputs</c>/<c>Factory</c> 최초 선언)과 9번 탭 카드3("INodeTypeDescriptor 확장" —
/// <see cref="PropertySchema"/> 추가) 두 카드를 하나로 합친 정식 통합판입니다(<c>NodeContext</c>·
/// <c>ITagHistorian</c>·<c>IStructureService</c>가 이미 썼던 "여러 카드에 걸친 부분 선언을 통합"
/// 원칙과 동일).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><see cref="Factory"/>와 <c>IFlowNode.Id</c> 동기화</b>: 02번 문서 2번 탭 카드1은
/// <c>IFlowNode</c> 자체가 "인스턴스 생성 방법을 정의하지 않는다"고 명시하고, 실제 생성·<c>Id</c>
/// 동기화는 이 <see cref="Factory"/> 델리게이트가 전담합니다 — <c>NodeConfig</c>를 받아 그 안의
/// <c>Id</c>/<c>Name</c>이 반영된 <see cref="IFlowNode"/> 인스턴스를 돌려줍니다. 직접 구현하는 대신
/// <c>NodeSharp.Registry.NodeTypeDescriptorBuilder&lt;TNode&gt;</c>(RG-01)를 쓰면, 대상 타입이
/// 매개변수 없는 공개 생성자만 가지고 있어도 반사(reflection)로 <c>Id</c>를 채워주는 기본 팩토리를
/// 자동으로 만들어줍니다(직접 <see cref="Factory"/>를 지정하면 그 팩토리가 우선).</item>
/// <item><b>수집 관례</b>: 각 노드 타입은 보통 자신의 <see cref="INodeTypeDescriptor"/> 인스턴스를
/// <c>public static readonly</c> 필드/프로퍼티(관례적으로 이름은 <c>Descriptor</c>)로 노출합니다
/// (02번 문서 9번 탭 카드3의 <c>HttpRequestNodeType.Descriptor</c> 예시) — <c>NodeTypeRegistry.ScanAssembly</c>
/// (RG-01)가 이 관례를 따라 어셈블리에서 <see cref="INodeTypeDescriptor"/> 타입의 정적 멤버를 찾아
/// 수집합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 빌더로 만들기(권장, NodeSharp.Registry.NodeTypeDescriptorBuilder&lt;TNode&gt;)
/// public sealed class HttpRequestNode : IFlowNode { /* ... */ }
///
/// public static class HttpRequestNodeType
/// {
///     public static readonly INodeTypeDescriptor Descriptor =
///         new NodeTypeDescriptorBuilder&lt;HttpRequestNode&gt;("http-request")
///             .WithCategory("network")
///             .WithProperty(new("url", "URL", PropertyFieldType.Text, Required: true,
///                 HelpText: "요청을 보낼 전체 주소입니다.", Example: "https://api.example.com/sensors/1"))
///             .Build();
/// }
///
/// // 2) NodeTypeRegistry가 이 어셈블리를 스캔하면 자동으로 등록되고, Factory가 Id/Name을 동기화
/// var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
/// registry.ScanAssembly(typeof(HttpRequestNodeType).Assembly);
/// var cfg = new NodeConfig("n1", "http-request", "센서 조회", "f1", new Dictionary&lt;string, object?&gt;());
/// IFlowNode node = registry.CreateInstance(cfg);
/// // node.Id == "n1" (Factory가 반사로 채움), node.Name == "센서 조회"
/// </code>
/// </example>
public interface INodeTypeDescriptor
{
    /// <summary>노드 타입 이름(예: <c>"function"</c>). <see cref="NodeConfig.Type"/>과 매칭되는 키입니다.</summary>
    string TypeName { get; }

    /// <summary>팔레트에서 이 노드가 속하는 분류(예: <c>"input"</c>/<c>"output"</c>/<c>"function"</c>/<c>"network"</c>).</summary>
    string Category { get; }

    /// <summary>팔레트·캔버스에 표시할 아이콘 글리프 이름(Editor Phase에서 실제로 사용).</summary>
    string IconGlyph { get; }

    /// <summary>이 노드 타입을 캔버스에 새로 놓았을 때 기본으로 갖는 입력 포트 개수.</summary>
    int DefaultInputs { get; }

    /// <summary>이 노드 타입을 캔버스에 새로 놓았을 때 기본으로 갖는 출력 포트 개수.</summary>
    int DefaultOutputs { get; }

    /// <summary>
    /// <see cref="NodeConfig"/>를 받아 실제 <see cref="IFlowNode"/> 인스턴스를 만드는 팩토리입니다.
    /// <see cref="IFlowNode.Id"/>/<see cref="IFlowNode.Name"/>을 <paramref name="cfg"/>와 동기화하는
    /// 책임을 이 델리게이트가 집니다(RG-01 완료 기준).
    /// </summary>
    Func<NodeConfig, IFlowNode> Factory { get; }

    /// <summary>노드 속성 편집 다이얼로그에 표시할 입력 필드 목록(9번 탭 카드3, <see cref="PropertyField"/> 참고).</summary>
    IReadOnlyList<PropertyField> PropertySchema { get; }
}
