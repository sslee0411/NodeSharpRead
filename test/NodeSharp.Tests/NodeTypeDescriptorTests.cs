using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="INodeTypeDescriptor"/>/<see cref="NodeTypeDescriptorBuilder{TNode}"/>/
/// <see cref="NodeTypeRegistry.ScanAssembly"/>(RG-01)의 동작을 검증합니다. 완료 기준(03번 Step맵
/// RG-01): "IFlowNode 구현체가 담긴 어셈블리를 로드했을 때 NodeRegistry가 타입 이름/카테고리/
/// PropertySchema 3가지를 정확히 수집해 목록으로 반환하는지" + 오래 미뤄뒀던(RT-01a부터의 여러
/// 주석 참고) "IFlowNode.Id를 NodeConfig.Id와 동기화" 완료 기준을 함께 검증합니다.
/// </summary>
public class NodeTypeDescriptorTests
{
    /// <summary>테스트용 최소 IFlowNode — Id는 init 세터를 가져 반사로 채울 수 있다(관례).</summary>
    private sealed class TestFunctionNode : IFlowNode
    {
        public string Id { get; init; } = string.Empty;
        public string Type => "test-function";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>02번 문서 9번 탭 카드3 관례 — 노드 타입 옆에 public static readonly Descriptor 필드를 둔다.</summary>
    private static class TestFunctionNodeType
    {
        public static readonly INodeTypeDescriptor Descriptor =
            new NodeTypeDescriptorBuilder<TestFunctionNode>("test-function")
                .WithCategory("function")
                .WithIcon("glyph-test")
                .WithPorts(inputs: 1, outputs: 1)
                .WithProperty(new PropertyField("code", "코드", PropertyFieldType.Code, Required: true,
                    HelpText: "테스트용 필드입니다.", Example: "return msg;"))
                .Build();
    }

    /// <summary>Id가 계산 전용(세터 없음)인 노드 — 반사 기반 바인딩이 예외 없이 그냥 건너뛰는지 확인용.</summary>
    private sealed class ReadOnlyIdNode : IFlowNode
    {
        public string Id => "fixed-id";
        public string Type => "readonly-id";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>레거시 RT-01a 경로(TryRegister(PluginManifest, Type)) 검증용 노드.</summary>
    private sealed class LegacyNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "legacy-node";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public void Builder는_체이닝한_값으로_INodeTypeDescriptor를_만든다()
    {
        var descriptor = TestFunctionNodeType.Descriptor;

        Assert.Equal("test-function", descriptor.TypeName);
        Assert.Equal("function", descriptor.Category);
        Assert.Equal("glyph-test", descriptor.IconGlyph);
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Single(descriptor.PropertySchema);
        Assert.Equal("code", descriptor.PropertySchema[0].Key);
    }

    [Fact]
    public void 완료_기준_직접_검증__기본_Factory는_반사로_Id를_NodeConfig_Id와_동기화하고_Name도_설정한다()
    {
        var cfg = new NodeConfig("n1", "test-function", "테스트 노드", "f1", new Dictionary<string, object?>());

        var node = TestFunctionNodeType.Descriptor.Factory(cfg);

        Assert.Equal("n1", node.Id);
        Assert.Equal("테스트 노드", node.Name);
    }

    [Fact]
    public void WithFactory로_직접_지정하면_기본_Factory_대신_그_팩토리가_쓰인다()
    {
        var customCalled = false;
        var descriptor = new NodeTypeDescriptorBuilder<TestFunctionNode>("custom-factory")
            .WithFactory(cfg =>
            {
                customCalled = true;
                return new TestFunctionNode { Id = "custom-" + cfg.Id, Name = cfg.Name };
            })
            .Build();
        var cfg = new NodeConfig("n1", "custom-factory", "이름", "f1", new Dictionary<string, object?>());

        var node = descriptor.Factory(cfg);

        Assert.True(customCalled);
        Assert.Equal("custom-n1", node.Id);
    }

    [Fact]
    public void Id_세터가_없는_노드_타입도_기본_Factory가_예외_없이_인스턴스를_만든다()
    {
        var descriptor = new NodeTypeDescriptorBuilder<ReadOnlyIdNode>("readonly-id").Build();
        var cfg = new NodeConfig("n1", "readonly-id", "이름", "f1", new Dictionary<string, object?>());

        var node = descriptor.Factory(cfg);

        Assert.Equal("fixed-id", node.Id);   // 세터가 없어 원래 값 그대로 — 예외 없이 무시됨
        Assert.Equal("이름", node.Name);
    }

    [Fact]
    public void 완료_기준_직접_검증__ScanAssembly는_INodeTypeDescriptor_정적_필드를_찾아_등록한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");

        var found = registry.ScanAssembly(typeof(NodeTypeDescriptorTests).Assembly);

        Assert.True(found >= 1);
        Assert.True(registry.Descriptors.ContainsKey("test-function"));
        Assert.Equal("function", registry.Descriptors["test-function"].Category);
        Assert.Single(registry.Descriptors["test-function"].PropertySchema);
    }

    [Fact]
    public void ScanAssembly는_같은_어셈블리를_다시_스캔해도_안전하게_덮어쓴다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");

        registry.ScanAssembly(typeof(NodeTypeDescriptorTests).Assembly);
        registry.ScanAssembly(typeof(NodeTypeDescriptorTests).Assembly);   // 재스캔

        Assert.True(registry.Descriptors.ContainsKey("test-function"));   // 예외 없이 그대로 유지
    }

    [Fact]
    public void 완료_기준_직접_검증__CreateInstance는_Descriptor로_등록된_타입을_Factory_경로로_생성해_Id를_동기화한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(NodeTypeDescriptorTests).Assembly);
        var cfg = new NodeConfig("n42", "test-function", "센서", "f1", new Dictionary<string, object?>());

        var node = registry.CreateInstance(cfg);

        Assert.Equal("n42", node.Id);
        Assert.Equal("센서", node.Name);
    }

    [Fact]
    public void 완료_기준_직접_검증__CreateInstance는_레거시_TryRegister_Type_경로도_이제_Id를_동기화한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("legacy-node", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(LegacyNode));
        var cfg = new NodeConfig("n7", "legacy-node", "레거시", "f1", new Dictionary<string, object?>());

        var node = registry.CreateInstance(cfg);

        Assert.Equal("n7", node.Id);   // ★ RG-01 이전에는 Activator가 준 랜덤 Guid 그대로였음(회귀 검증)
        Assert.Equal("레거시", node.Name);
    }

    [Fact]
    public void CreateInstance는_Descriptor와_TryRegister_어디에도_없으면_예외를_던진다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var cfg = new NodeConfig("n1", "no-such-type", "없음", "f1", new Dictionary<string, object?>());

        Assert.Throws<InvalidOperationException>(() => registry.CreateInstance(cfg));
    }
}
