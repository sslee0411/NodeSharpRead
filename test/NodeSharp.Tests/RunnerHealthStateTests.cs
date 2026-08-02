using System.Text.Json;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runner.Health;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="RunnerHealthState"/>·<see cref="RunnerHealthSnapshot"/>(RN-04a)에 대한 테스트입니다.
/// 완료 기준(03번 Step맵 RN-04a): "/health 호출 시 Uptime·배포 노드 수·실패 노드 목록 3개 값이
/// JSON으로 반환되는지 확인".
/// </summary>
public class RunnerHealthStateTests
{
    /// <summary>이 테스트 파일 전용 — OnStartAsync에서 항상 성공하는 더미 노드.</summary>
    private sealed class OkTestNode : IFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Type => "health-ok";
        public string Name { get; set; } = "OK";
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>이 테스트 파일 전용 — OnStartAsync에서 항상 예외를 던져 FailedNodeIds에 잡히는 더미 노드.</summary>
    private sealed class FailingTestNode : IFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Type => "health-fail";
        public string Name { get; set; } = "Fail";
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => throw new InvalidOperationException("일부러 실패");
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public void 완료_기준_직접_검증__배포_전_Snapshot은_배포_노드_수_0에_실패_목록도_빈_상태다()
    {
        var state = new RunnerHealthState();

        var snapshot = state.Snapshot();

        Assert.Equal("Healthy", snapshot.Status);
        Assert.True(snapshot.UptimeSeconds >= 0);
        Assert.Equal(0, snapshot.DeployedNodeCount);
        Assert.Null(snapshot.LastDeployAt);
        Assert.Empty(snapshot.FailedNodeIds);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__RecordDeploy_후_Snapshot이_실제_배포_결과를_반영한다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("health-ok", "1.0.0", "1.0.0"), typeof(OkTestNode));
        registry.TryRegister(new PluginManifest("health-fail", "1.0.0", "1.0.0"), typeof(FailingTestNode));

        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(
            Id: "f1", Name: "헬스 테스트 플로우",
            Nodes: new List<NodeConfig>
            {
                new("n1", "health-ok", "정상", "f1", new Dictionary<string, object?>()),
                new("n2", "health-fail", "실패", "f1", new Dictionary<string, object?>()),
            },
            Wires: new List<Wire>());
        await engine.DeployAsync(flow, CancellationToken.None);

        var state = new RunnerHealthState();
        state.RecordDeploy(engine);
        var snapshot = state.Snapshot();

        Assert.Equal(engine.Nodes.Count, snapshot.DeployedNodeCount);
        Assert.NotNull(snapshot.LastDeployAt);
        Assert.Contains("n2", snapshot.FailedNodeIds);
    }

    [Fact]
    public void 완료_기준_직접_검증__JSON_직렬화_결과에_4개_속성_이름이_모두_포함된다()
    {
        var state = new RunnerHealthState();

        var json = JsonSerializer.Serialize(state.Snapshot());

        Assert.Contains("UptimeSeconds", json);
        Assert.Contains("DeployedNodeCount", json);
        Assert.Contains("LastDeployAt", json);
        Assert.Contains("FailedNodeIds", json);
    }
}
