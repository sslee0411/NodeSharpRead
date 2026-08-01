using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.CreateInstance"/>(RT-01a, 02번 설계 문서 2번 탭 카드 4·9 — Phase 2 첫 Step)에 대한
/// 단위 테스트입니다. 완료 기준: 타입 이름 문자열을 CreateInstance에 전달하면 해당 IFlowNode 인스턴스가
/// 반환되고, 존재하지 않는 타입은 예외로 명확히 구분되는지 확인.
/// </summary>
public class FlowEngineCreateInstanceTests
{
    /// <summary>테스트 전용 더미 노드 — LssLibNodeAdapterBase 관례(공개 매개변수 없는 생성자)를 그대로 따름.</summary>
    private sealed class FakeInjectNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "inject";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static NodeConfig BuildConfig(string type) =>
        new("n1", type, "테스트 노드", "f1", new Dictionary<string, object?>());

    [Fact]
    public void CreateInstance는_등록된_타입이면_해당_타입의_인스턴스를_반환한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("inject", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(FakeInjectNode));
        var engine = new FlowEngine(registry);

        IFlowNode node = engine.CreateInstance(BuildConfig("inject"));

        Assert.IsType<FakeInjectNode>(node);
    }

    [Fact]
    public void CreateInstance는_NodeConfig_Name을_생성된_노드에_반영한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("inject", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(FakeInjectNode));
        var engine = new FlowEngine(registry);

        IFlowNode node = engine.CreateInstance(BuildConfig("inject"));

        Assert.Equal("테스트 노드", node.Name);
    }

    [Fact]
    public void CreateInstance는_등록되지_않은_타입이면_InvalidOperationException을_던진다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry);

        Assert.Throws<InvalidOperationException>(() => engine.CreateInstance(BuildConfig("no-such-type")));
    }

    [Fact]
    public void INodeRegistry_CreateInstance를_직접_호출해도_동일하게_동작한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("inject", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(FakeInjectNode));

        INodeRegistry asRegistry = registry;   // TryRegister는 구현체 전용, CreateInstance는 인터페이스 경유로 호출해 계약 자체를 검증
        IFlowNode node = asRegistry.CreateInstance(BuildConfig("inject"));

        Assert.IsType<FakeInjectNode>(node);
    }
}
