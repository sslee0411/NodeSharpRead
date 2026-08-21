using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.PlcTagWrite;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="PlcTagWriteNode"/>/<see cref="PlcTagWriteNodeType"/>(ED-D06a, 03번 개발 Step맵 —
/// PLC Write 안전장치)에 대한 단위/통합 테스트입니다. 이 Step의 완료 기준("범위를 벗어난 값 쓰기는
/// 거부되고, 같은 태그에 대한 동시 쓰기 요청 중 하나는 락으로 대기하는지 확인")은 실제 PLC 연결
/// 여부와 무관하게 검증 가능하도록 설계되어(클래스 문서 참고), 여기서 xUnit만으로 완전히 증명합니다
/// (PD-01a와 동일한 선례 — 실제 PLC 하드웨어·WPF 런타임이 필요 없는 Step).
/// </summary>
public class PlcTagWriteNodeTests
{
    /// <summary>입력을 받아 인스턴스별 리스트에 기록만 하는 테스트 전용 수신 노드(PlcTagReadNodeTests와 동일한 패턴).</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public List<object?> Received { get; } = new();

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "receiver";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
        {
            Received.Add(msg.Payload);
            return Task.CompletedTask;
        }

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 위임만 하는, 상태 기록이 필요 없는 테스트 전용 <see cref="INodeContext"/>(PlcTagReadNodeTests.NoopNodeContext와 동일한 골격).</summary>
    private sealed class NoopNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public NoopNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
        public void Debug(string nodeName, string msgJson) { }
    }

    /// <summary>수신 노드 r0 하나를 "tagwrite"의 0번 출력 포트에 와이어로 연결해 배포한다(PlcTagReadNodeTests.BuildWireOnlyEngine과 동일한 패턴).</summary>
    private static (FlowEngine Engine, ReceiverNode Receiver) BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));

        var receiverConfig = new NodeConfig("r0", "receiver", "r0", "f1", new Dictionary<string, object?>());
        var wires = new List<Wire> { new Wire("tagwrite", 0, "r0", 0) };

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { receiverConfig }, Wires: wires);
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        return (engine, (ReceiverNode)engine.Nodes["r0"]);
    }

    [Fact]
    public void PlcTagWriteNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(PlcTagWriteNodeType).Assembly);

        Assert.True(registry.Descriptors.ContainsKey("plcTagWrite"));
        var descriptor = registry.Descriptors["plcTagWrite"];
        Assert.Equal(1, descriptor.DefaultInputs);
        Assert.Equal(1, descriptor.DefaultOutputs);
        Assert.Equal(3, descriptor.PropertySchema.Count);
        Assert.Equal("tagId", descriptor.PropertySchema[0].Key);
        Assert.Equal(PropertyFieldType.TagRef, descriptor.PropertySchema[0].Type);
        Assert.True(descriptor.PropertySchema[0].Required);
        Assert.Equal("minValue", descriptor.PropertySchema[1].Key);
        Assert.Equal(PropertyFieldType.Number, descriptor.PropertySchema[1].Type);
        Assert.False(descriptor.PropertySchema[1].Required);
        Assert.Equal("maxValue", descriptor.PropertySchema[2].Key);
        Assert.Equal(PropertyFieldType.Number, descriptor.PropertySchema[2].Type);
    }

    [Fact]
    public void Factory는_tagId와_minValue_maxValue를_그대로_읽는다()
    {
        var cfg = new NodeConfig("n1", "plcTagWrite", "테스트", "f1", new Dictionary<string, object?>
        {
            ["tagId"] = "abc-123",
            ["minValue"] = 0.0,
            ["maxValue"] = 100.0,
        });
        var node = (PlcTagWriteNode)PlcTagWriteNodeType.Descriptor.Factory(cfg);

        Assert.Equal("abc-123", node.TagId);
        Assert.Equal(0.0, node.MinValue);
        Assert.Equal(100.0, node.MaxValue);
    }

    [Fact]
    public void Factory는_minValue_maxValue가_없으면_null을_쓴다()
    {
        var cfg = new NodeConfig("n1", "plcTagWrite", "테스트", "f1", new Dictionary<string, object?>
        {
            ["tagId"] = "abc-123",
        });
        var node = (PlcTagWriteNode)PlcTagWriteNodeType.Descriptor.Factory(cfg);

        Assert.Equal("abc-123", node.TagId);
        Assert.Null(node.MinValue);
        Assert.Null(node.MaxValue);
    }

    [Fact]
    public async Task 범위를_벗어난_값은_거부되고_WriteAction과_라우팅_모두_일어나지_않는다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var writeCount = 0;
        var node = new PlcTagWriteNode
        {
            Id = "tagwrite",
            Name = "태그쓰기",
            TagId = "tag-guid-1",
            MinValue = 0,
            MaxValue = 100,
            WriteAction = (_, _) => { Interlocked.Increment(ref writeCount); return Task.CompletedTask; },
        };
        var ctx = new NoopNodeContext(engine);

        await node.OnInputAsync(new Msg { Payload = -1.0 }, ctx, CancellationToken.None);
        await node.OnInputAsync(new Msg { Payload = 101.0 }, ctx, CancellationToken.None);

        Assert.Equal(0, writeCount);
        Assert.Empty(receiver.Received);
    }

    [Fact]
    public async Task 범위_안의_값은_WriteAction이_호출되고_다음_노드로_라우팅된다()
    {
        var (engine, receiver) = BuildWireOnlyEngine();
        var writtenValues = new List<double>();
        var node = new PlcTagWriteNode
        {
            Id = "tagwrite",
            Name = "태그쓰기",
            TagId = "tag-guid-2",
            MinValue = 0,
            MaxValue = 100,
            WriteAction = (v, _) => { writtenValues.Add(v); return Task.CompletedTask; },
        };
        var ctx = new NoopNodeContext(engine);

        await node.OnInputAsync(new Msg { Payload = 42.0 }, ctx, CancellationToken.None);

        Assert.Single(writtenValues);
        Assert.Equal(42.0, writtenValues[0]);
        Assert.Single(receiver.Received);
        Assert.Equal(42.0, receiver.Received[0]);
    }

    [Fact]
    public async Task 같은_TagId를_가리키는_동시_쓰기_요청은_락으로_직렬화된다()
    {
        // 완료 기준: "같은 태그에 대한 동시 쓰기 요청 중 하나는 락으로 대기하는지 확인" — WriteAction
        // 안에서 지연을 주며 최대 동시 실행 수를 세어, 절대 1을 넘지 않는지 증명한다(클래스 remarks의
        // "락은 TagId 기준" 설계를 실행 시점에 확인).
        var engine1 = new FlowEngine(new NodeTypeRegistry(contractsVersion: "1.0.0"));
        var ctx = new NoopNodeContext(engine1);

        var sharedTagId = "tag-guid-shared-" + Guid.NewGuid().ToString("N");
        var currentConcurrency = 0;
        var peakConcurrency = 0;
        var gate = new object();

        Func<double, CancellationToken, Task> writeAction = async (_, ct) =>
        {
            lock (gate)
            {
                currentConcurrency++;
                if (currentConcurrency > peakConcurrency) peakConcurrency = currentConcurrency;
            }
            await Task.Delay(50, ct);
            lock (gate)
            {
                currentConcurrency--;
            }
        };

        var nodeA = new PlcTagWriteNode { Id = "tagwriteA", Name = "쓰기A", TagId = sharedTagId, WriteAction = writeAction };
        var nodeB = new PlcTagWriteNode { Id = "tagwriteB", Name = "쓰기B", TagId = sharedTagId, WriteAction = writeAction };

        await Task.WhenAll(
            nodeA.OnInputAsync(new Msg { Payload = 1.0 }, ctx, CancellationToken.None),
            nodeB.OnInputAsync(new Msg { Payload = 2.0 }, ctx, CancellationToken.None));

        Assert.Equal(1, peakConcurrency);
    }

    [Fact]
    public void DeployAsync는_PlcTagWrite_노드를_다른_노드와_동일하게_정상_배포한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.ScanAssembly(typeof(PlcTagWriteNodeType).Assembly);

        var cfg = new NodeConfig("tagwrite", "plcTagWrite", "태그쓰기", "f1", new Dictionary<string, object?>
        {
            ["tagId"] = "tag-guid-1",
        });

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(Id: "f1", Name: "테스트 플로우",
            Nodes: new List<NodeConfig> { cfg }, Wires: new List<Wire>());
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();

        Assert.DoesNotContain("tagwrite", engine.FailedNodeIds);
    }
}
