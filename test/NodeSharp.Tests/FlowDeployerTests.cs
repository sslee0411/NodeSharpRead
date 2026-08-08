using System.Text;
using System.Text.Json;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runner;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowDeployer"/>·<see cref="NodeStatusConsoleLogger"/>(RN-02)에 대한 테스트입니다.
/// 완료 기준(03번 Step맵 RN-02): "더미 노드 1개를 DeployAsync한 뒤 콘솔에 상태 로그가 출력되는지
/// 확인(Inject/Function/Switch/Debug 동작 확인은 Phase 8 LK-02에서 별도 수행)".
/// </summary>
public class FlowDeployerTests
{
    /// <summary>
    /// 이 테스트 파일 전용 더미 노드입니다. 실제 Inject/Function 등은 Phase 7~8에서 구현되므로,
    /// 배포 메커니즘 자체(등록→인스턴스 생성→OnStartAsync→SetStatus→콘솔 출력)만 확인합니다.
    /// </summary>
    private sealed class StatusPingTestNode : IFlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Type => "status-ping";
        public string Name { get; set; } = "Status Ping";
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
        {
            ctx.SetStatus("green", "dot", "실행 중");
            return Task.CompletedTask;
        }

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nodesharp-flowdeployer-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static NodeTypeRegistry NewRegistryWithStatusPing()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("status-ping", "1.0.0", "1.0.0"), typeof(StatusPingTestNode));
        return registry;
    }

    [Fact]
    public async Task 완료_기준_직접_검증__더미_노드_1개를_배포하면_콘솔에_상태_로그가_출력된다()
    {
        var dir = NewTempDir();
        var originalOut = Console.Out;
        try
        {
            // (★ EC-05 확장) flows.json은 이제 FlowDefinition 목록(Flow 탭 개수만큼) — 클래스 주석 참고.
            var flow = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig>
                {
                    new("n1", "status-ping", "핑", "f1", new Dictionary<string, object?>()),
                },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow }));

            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: true, ErrorMessage: null) };

            var writer = new StringWriter();
            Console.SetOut(writer);

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Console.SetOut(originalOut);

            Assert.NotNull(engine);
            var output = writer.ToString();
            Assert.Contains("n1", output);
            Assert.Contains("실행 중", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EC05_확인_기준__여러_Flow_탭의_노드가_모두_한번에_병합_배포된다()
    {
        // (v2.51 신설, ★ 사용자 요청) EC-05 다중 Flow 탭 — flows.json에 탭 2개가 있으면 둘 다 동시에
        // 배포돼야 한다(실제 Node-RED처럼 모든 활성 탭이 항상 함께 동작).
        var dir = NewTempDir();
        var originalOut = Console.Out;
        try
        {
            var tab1 = new FlowDefinition(
                Id: "f1", Name: "1호기 라인",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑1", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            var tab2 = new FlowDefinition(
                Id: "f2", Name: "2호기 라인",
                Nodes: new List<NodeConfig> { new("n2", "status-ping", "핑2", "f2", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { tab1, tab2 }));

            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: true, ErrorMessage: null) };

            var writer = new StringWriter();
            Console.SetOut(writer);

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Console.SetOut(originalOut);

            Assert.NotNull(engine);
            var output = writer.ToString();
            Assert.Contains("n1", output); // 1호기 라인 노드
            Assert.Contains("n2", output); // 2호기 라인 노드도 함께 배포됨
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EC05_확인_기준__Disabled인_탭의_노드는_배포되지_않는다()
    {
        // (v2.51 신설, ★ 사용자 요청) FlowDefinition.Disabled=true인 탭은 그 탭에 속한 노드가
        // 하나도 생성되지 않아야 한다(FlowDefinition.Disabled 원래 XML 문서 그대로).
        var dir = NewTempDir();
        var originalOut = Console.Out;
        try
        {
            var activeTab = new FlowDefinition(
                Id: "f1", Name: "1호기 라인",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑1", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            var disabledTab = new FlowDefinition(
                Id: "f2", Name: "2호기 라인(점검 중)",
                Nodes: new List<NodeConfig> { new("n2", "status-ping", "핑2", "f2", new Dictionary<string, object?>()) },
                Wires: new List<Wire>(),
                Disabled: true);
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { activeTab, disabledTab }));

            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: true, ErrorMessage: null) };

            var writer = new StringWriter();
            Console.SetOut(writer);

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Console.SetOut(originalOut);

            Assert.NotNull(engine);
            Assert.Contains("n1", engine!.Nodes.Keys);
            Assert.DoesNotContain("n2", engine.Nodes.Keys); // Disabled 탭의 노드는 생성 자체가 안 됨
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EC05_확인_기준__모든_탭이_Disabled면_배포하지_않는다()
    {
        var dir = NewTempDir();
        try
        {
            var disabledTab = new FlowDefinition(
                Id: "f1", Name: "점검 중",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>(),
                Disabled: true);
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { disabledTab }));

            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: true, ErrorMessage: null) };

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Assert.Null(engine);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__flows_json_단계가_실패했으면_배포를_시도하지_않는다()
    {
        var dir = NewTempDir();
        try
        {
            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: false, ErrorMessage: "손상됨") };

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Assert.Null(engine);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__flows_json_단계가_결과_목록에_없으면_배포를_시도하지_않는다()
    {
        var dir = NewTempDir();
        try
        {
            var stages = new List<StartupStageResult> { new("device.json", Succeeded: true, ErrorMessage: null) };

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None);

            Assert.Null(engine);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
