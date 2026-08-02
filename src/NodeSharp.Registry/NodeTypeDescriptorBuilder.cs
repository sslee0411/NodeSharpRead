using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 노드 타입 디스크립터 빌더
/// 역활 및 기능 : INodeTypeDescriptor를 체이닝 방식으로 조립하는 제네릭 빌더(기본 Factory는 반사로 Id 동기화)
///
/// <see cref="INodeTypeDescriptor"/>를 만드는 유창한(fluent) 빌더입니다(RG-01, 02번 문서 9번 탭 카드3
/// <c>HttpRequestNodeType</c> 예시의 <c>NodeTypeDescriptorBuilder</c> 사용법 그대로). 원본 예시는
/// <c>WithFactory</c> 호출 없이 <c>.Build()</c>만 하는데, <see cref="INodeTypeDescriptor.Factory"/>를
/// 어떻게 채우는지는 정식 선언이 없던 공백이었습니다 — 이 빌더를 제네릭(<c>TNode</c>, 공개 매개변수
/// 없는 생성자 필요)으로 만들어, <see cref="WithFactory"/>를 호출하지 않으면
/// <c>() =&gt; new TNode()</c> 후 <see cref="NodeIdBinder.Bind"/>로 <c>Id</c>를, <c>Name</c>은 직접
/// 대입하는 기본 팩토리를 자동으로 만들어주도록 해소했습니다(직접 <see cref="WithFactory"/>를 부르면
/// 그 팩토리가 우선).
/// </summary>
/// <example>
/// <code>
/// public sealed class FunctionNode : IFlowNode
/// {
///     public string Id { get; init; } = string.Empty;
///     public string Type => "function";
///     public string Name { get; set; } = string.Empty;
///     // ... InputPorts/OutputPorts/OnStartAsync/OnInputAsync/OnCloseAsync ...
/// }
///
/// public static class FunctionNodeType
/// {
///     public static readonly INodeTypeDescriptor Descriptor =
///         new NodeTypeDescriptorBuilder&lt;FunctionNode&gt;("function")
///             .WithCategory("function")
///             .WithPorts(inputs: 1, outputs: 1)
///             .WithProperty(new("code", "코드", PropertyFieldType.Code, Required: true,
///                 HelpText: "NCalc 표현식 또는 C# 코드.", Example: "return msg.payload * 1.8 + 32;"))
///             .Build();   // WithFactory 생략 — 기본 팩토리가 반사로 Id 동기화
/// }
/// </code>
/// </example>
public sealed class NodeTypeDescriptorBuilder<TNode> where TNode : IFlowNode, new()
{
    private readonly string _typeName;
    private string _category = "function";
    private string _iconGlyph = string.Empty;
    private int _defaultInputs = 1;
    private int _defaultOutputs = 1;
    private readonly List<PropertyField> _properties = new();
    private Func<NodeConfig, IFlowNode>? _factory;

    /// <summary><paramref name="typeName"/>은 <see cref="NodeConfig.Type"/>과 매칭될 노드 타입 이름입니다.</summary>
    public NodeTypeDescriptorBuilder(string typeName) => _typeName = typeName;

    /// <summary>팔레트 분류를 지정합니다(예: <c>"input"</c>/<c>"output"</c>/<c>"function"</c>/<c>"network"</c>).</summary>
    public NodeTypeDescriptorBuilder<TNode> WithCategory(string category)
    {
        _category = category;
        return this;
    }

    /// <summary>팔레트·캔버스 아이콘 글리프를 지정합니다.</summary>
    public NodeTypeDescriptorBuilder<TNode> WithIcon(string iconGlyph)
    {
        _iconGlyph = iconGlyph;
        return this;
    }

    /// <summary>캔버스에 새로 놓았을 때 기본 입력/출력 포트 개수를 지정합니다.</summary>
    public NodeTypeDescriptorBuilder<TNode> WithPorts(int inputs, int outputs)
    {
        _defaultInputs = inputs;
        _defaultOutputs = outputs;
        return this;
    }

    /// <summary>속성 편집 다이얼로그에 표시할 입력 필드를 하나 추가합니다. 호출 순서대로 <see cref="INodeTypeDescriptor.PropertySchema"/>에 쌓입니다.</summary>
    public NodeTypeDescriptorBuilder<TNode> WithProperty(PropertyField field)
    {
        _properties.Add(field);
        return this;
    }

    /// <summary>
    /// 기본 팩토리(반사로 <c>Id</c> 동기화) 대신 직접 만든 팩토리를 쓰고 싶을 때 지정합니다 —
    /// 매개변수 없는 생성자가 아닌 다른 생성 방식이 필요한 노드 타입에 사용합니다.
    /// </summary>
    public NodeTypeDescriptorBuilder<TNode> WithFactory(Func<NodeConfig, IFlowNode> factory)
    {
        _factory = factory;
        return this;
    }

    /// <summary>지금까지 설정한 값으로 <see cref="INodeTypeDescriptor"/>를 완성합니다.</summary>
    public INodeTypeDescriptor Build()
    {
        var factory = _factory ?? DefaultFactory;
        return new Descriptor(_typeName, _category, _iconGlyph, _defaultInputs, _defaultOutputs, factory, _properties.ToArray());
    }

    private static IFlowNode DefaultFactory(NodeConfig cfg)
    {
        var node = new TNode();
        NodeIdBinder.Bind(node, cfg.Id);
        node.Name = cfg.Name;
        return node;
    }

    private sealed record Descriptor(
        string TypeName,
        string Category,
        string IconGlyph,
        int DefaultInputs,
        int DefaultOutputs,
        Func<NodeConfig, IFlowNode> Factory,
        IReadOnlyList<PropertyField> PropertySchema) : INodeTypeDescriptor;
}
