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
/// 확인(Inject/Function/Switch/Debug 동작 확인은 Phase 8 LK-02에서 별도 수행)". (LK-01) 완료 기준
/// "flows.json.signal 변경 감지 → 자동 재배포"에서 "자동 재배포" 부분(<see cref="FlowDeployer.RedeployAsync"/>)도
/// 이 클래스가 검증합니다 — "감지" 부분(<see cref="FileSystemWatcher"/> 연동)은
/// <c>FlowFileWatcherTests</c>가 별도로 다룹니다. (LK-02a) <c>attachMonitor</c> 콜백이 진짜 새
/// <c>FlowEngine</c>이 만들어질 때만 호출되고(엔진 재사용 시 재호출되지 않음) 정확한 <c>EventBus</c>를
/// 받는지도 이 클래스가 검증합니다 — <c>StatusBroadcaster</c> 자체의 SignalR 중계 로직은
/// <c>StatusBroadcasterTests</c>가 별도로 다룹니다.
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

    [Fact]
    public async Task RedeployAsync는_기존_엔진이_없으면_새로_만들어_배포한다()
    {
        // (LK-01) 부팅 시점엔 flows.json이 없었지만 Editor가 그 뒤 처음 저장한 상황을 시뮬레이션.
        var dir = NewTempDir();
        try
        {
            var flow = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow }));

            var engine = await new FlowDeployer().RedeployAsync(
                existingEngine: null, dir, NewRegistryWithStatusPing(), CancellationToken.None);

            Assert.NotNull(engine);
            Assert.Contains("n1", engine!.Nodes.Keys);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RedeployAsync는_기존_엔진이_있으면_새_엔진을_만들지_않고_같은_인스턴스에_재배포한다()
    {
        // (LK-01) 완료 기준 근거: 매번 새 FlowEngine을 만들면 이전 엔진의 노드(예: Inject 타이머)가
        // OnCloseAsync 없이 버려지는 누수가 생긴다 — 같은 인스턴스를 재사용해야 DeployAsync의 기존
        // 노드 정리(OnCloseAsync) 경로를 탄다(FlowDeployer.cs 클래스 remarks의 LK-01 항목 참고).
        var dir = NewTempDir();
        try
        {
            var flow1 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑1", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow1 }));

            var registry = NewRegistryWithStatusPing();
            var deployer = new FlowDeployer();
            var firstEngine = await deployer.RedeployAsync(existingEngine: null, dir, registry, CancellationToken.None);
            Assert.NotNull(firstEngine);

            var flow2 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n2", "status-ping", "핑2", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow2 }));

            var secondEngine = await deployer.RedeployAsync(firstEngine, dir, registry, CancellationToken.None);

            Assert.Same(firstEngine, secondEngine); // 참조 동일 = 새 엔진을 만들지 않고 재사용됨
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RedeployAsync는_이전_노드를_정리하고_새_flows_json_기준으로_노드를_교체한다()
    {
        // (LK-01) 완료 기준 직접 검증: 재배포가 실제로 flows.json의 최신 내용을 반영하는지 —
        // n1만 있던 배포를 n2만 있는 flows.json으로 재배포하면 n1은 사라지고 n2만 남아야 한다.
        var dir = NewTempDir();
        try
        {
            var flow1 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑1", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow1 }));

            var registry = NewRegistryWithStatusPing();
            var deployer = new FlowDeployer();
            var engine = await deployer.RedeployAsync(existingEngine: null, dir, registry, CancellationToken.None);
            Assert.Contains("n1", engine!.Nodes.Keys);

            var flow2 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n2", "status-ping", "핑2", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow2 }));

            engine = await deployer.RedeployAsync(engine, dir, registry, CancellationToken.None);

            Assert.DoesNotContain("n1", engine!.Nodes.Keys);
            Assert.Contains("n2", engine.Nodes.Keys);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RedeployAsync는_flows_json이_없으면_기존_엔진을_그대로_반환한다()
    {
        var dir = NewTempDir();
        try
        {
            // flows.json을 아예 만들지 않음 — 신호가 잘못 왔거나 아직 저장 전인 상황.
            var registry = NewRegistryWithStatusPing();
            var deployer = new FlowDeployer();

            var resultWhenNull = await deployer.RedeployAsync(existingEngine: null, dir, registry, CancellationToken.None);
            Assert.Null(resultWhenNull);

            // 이미 배포된 엔진이 있는 상태에서 flows.json이 사라진(또는 아직 없는) 경우 — 있던 걸 지우지 않음.
            var flow = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            var dir2 = NewTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir2, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow }));
                var existingEngine = await deployer.RedeployAsync(existingEngine: null, dir2, registry, CancellationToken.None);

                var resultWhenMissing = await deployer.RedeployAsync(existingEngine, dir, registry, CancellationToken.None); // dir(flows.json 없음)로 재배포 시도
                Assert.Same(existingEngine, resultWhenMissing);
            }
            finally
            {
                Directory.Delete(dir2, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DeployIfAvailableAsync는_attachMonitor를_새_엔진의_EventBus로_정확히_한_번_호출한다()
    {
        // (LK-02a) attachMonitor 콜백이 실제로 배선되는지, 그리고 "그 엔진의 EventBus"가 넘어오는지 확인.
        var dir = NewTempDir();
        try
        {
            var flow = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow }));
            var stages = new List<StartupStageResult> { new("flows.json", Succeeded: true, ErrorMessage: null) };

            var callCount = 0;
            IEventBus? received = null;
            Func<IEventBus, IDisposable> attachMonitor = eventBus =>
            {
                callCount++;
                received = eventBus;
                return new NoOpDisposable();
            };

            var engine = await new FlowDeployer().DeployIfAvailableAsync(
                dir, stages, NewRegistryWithStatusPing(), CancellationToken.None, attachMonitor);

            Assert.NotNull(engine);
            Assert.Equal(1, callCount);
            Assert.Same(engine!.EventBus, received);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RedeployAsync는_기존_엔진을_재사용할_때_attachMonitor를_다시_호출하지_않는다()
    {
        // (LK-02a) 완료 기준 근거: 같은 엔진에 StatusBroadcaster가 중복 구독되면 SignalR로 같은
        // 이벤트가 여러 번 전송된다 — CreateEngineWithLogger(진짜 새 엔진을 만드는 유일한 지점)에서만
        // attachMonitor를 호출해야 하므로, 엔진 재사용 시(RedeployAsync가 existingEngine을 그대로
        // DeployAsync에 넘길 때) callCount가 늘어나지 않아야 한다.
        var dir = NewTempDir();
        try
        {
            var flow1 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n1", "status-ping", "핑1", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow1 }));

            var callCount = 0;
            Func<IEventBus, IDisposable> attachMonitor = eventBus =>
            {
                callCount++;
                return new NoOpDisposable();
            };

            var registry = NewRegistryWithStatusPing();
            var deployer = new FlowDeployer();
            var firstEngine = await deployer.RedeployAsync(existingEngine: null, dir, registry, CancellationToken.None, attachMonitor);
            Assert.Equal(1, callCount);   // 최초 생성 1회

            var flow2 = new FlowDefinition(
                Id: "f1", Name: "테스트 플로우",
                Nodes: new List<NodeConfig> { new("n2", "status-ping", "핑2", "f1", new Dictionary<string, object?>()) },
                Wires: new List<Wire>());
            File.WriteAllText(Path.Combine(dir, "flows.json"), JsonSerializer.Serialize(new List<FlowDefinition> { flow2 }));

            var secondEngine = await deployer.RedeployAsync(firstEngine, dir, registry, CancellationToken.None, attachMonitor);

            Assert.Same(firstEngine, secondEngine);
            Assert.Equal(1, callCount);   // 재배포(엔진 재사용)에서는 다시 호출되지 않음
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>attachMonitor 테스트에서 실제 구독 없이 <see cref="IDisposable"/> 계약만 충족하기 위한 무동작 더미.</summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
