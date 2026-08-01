using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowEngine.DeployAsync"/>의 CreateInstance/OnStartAsync 두 단계 예외 격리(RT-02b, 02번
/// 설계 문서 2번 탭 카드4 원본·3번 탭 카드6)에 대한 단위 테스트입니다. 완료 기준: 노드 하나에 문제가
/// 있어도(타입을 찾을 수 없거나, 생성자에서 예외를 던지거나, OnStartAsync에서 예외를 던지거나) 나머지
/// 노드는 정상 동작하는지 확인.
/// </summary>
public class FlowEngineExceptionIsolationTests
{
    /// <summary>정상 동작하는 테스트 노드 — 기동 여부를 정적 로그에 기록.</summary>
    private sealed class GoodNode : IFlowNode
    {
        public static readonly List<string> StartedNames = new();
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "good-node";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
        {
            StartedNames.Add(Name);
            return Task.CompletedTask;
        }

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>생성자에서 예외를 던지는 노드 — "타입은 찾았지만 인스턴스화 자체가 실패"하는 경우를 시뮬레이션.</summary>
    private sealed class ThrowingConstructorNode : IFlowNode
    {
        public ThrowingConstructorNode() => throw new InvalidOperationException("생성자 실패 테스트");

        public string Id { get; init; } = string.Empty;
        public string Type => "throwing-ctor";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>OnStartAsync에서 예외를 던지는 노드 — "잘못된 IP 주소" 같은 기동 실패를 시뮬레이션.</summary>
    private sealed class ThrowingOnStartNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "throwing-start";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => throw new InvalidOperationException("기동 실패 테스트(예: 잘못된 IP)");
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static FlowEngine BuildEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("good-node", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(GoodNode));
        registry.TryRegister(new PluginManifest("throwing-ctor", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ThrowingConstructorNode));
        registry.TryRegister(new PluginManifest("throwing-start", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ThrowingOnStartNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public async Task DeployAsync는_생성자에서_예외를_던지는_노드도_MissingNode로_흡수하고_예외_없이_완료된다()
    {
        var engine = BuildEngine();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { new NodeConfig("n1", "throwing-ctor", "생성자 폭발", "f1", new Dictionary<string, object?>()) },
            Wires: Array.Empty<Wire>());

        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.IsType<MissingNode>(engine.Nodes["n1"]);
    }

    [Fact]
    public async Task DeployAsync는_OnStartAsync_예외가_나도_해당_노드만_FailedNodeIds에_기록하고_나머지는_정상_기동한다()
    {
        GoodNode.StartedNames.Clear();
        var engine = BuildEngine();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[]
            {
                new NodeConfig("n1", "good-node", "노드1", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n2", "throwing-start", "잘못된 IP 노드", "f1", new Dictionary<string, object?>()),
                new NodeConfig("n3", "good-node", "노드3", "f1", new Dictionary<string, object?>()),
            },
            Wires: Array.Empty<Wire>());

        // ★ RT-02b: 예외 없이 완료되어야 한다
        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.Equal(new[] { "노드1", "노드3" }, GoodNode.StartedNames);
        Assert.Single(engine.FailedNodeIds);
        Assert.Equal("n2", engine.FailedNodeIds[0]);   // NodeConfig.Id 기록 확인(IFlowNode.Id 아님)
    }

    [Fact]
    public async Task DeployAsync는_모든_노드가_정상이면_FailedNodeIds가_비어있다()
    {
        GoodNode.StartedNames.Clear();
        var engine = BuildEngine();
        var flow = new FlowDefinition(
            Id: "f1", Name: "테스트 플로우",
            Nodes: new[] { new NodeConfig("n1", "good-node", "노드1", "f1", new Dictionary<string, object?>()) },
            Wires: Array.Empty<Wire>());

        await engine.DeployAsync(flow, CancellationToken.None);

        Assert.Empty(engine.FailedNodeIds);
    }
}
