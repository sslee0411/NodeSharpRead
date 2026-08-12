using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Inject;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="InjectNode"/>/<see cref="InjectNodeType"/>(NR-03a, 03번 개발 Step맵 Phase 7 — Inject
/// 노드의 첫 구현체)에 대한 통합 테스트입니다. 완료 기준(03번 Step맵 NR-03a): "Inject 버튼 클릭 시
/// 정확히 1회 Msg가 다음 노드로 전달되는지 확인" — Editor→Runner IPC(LK-02, Phase 8)가 아직 없어
/// 실제 WPF 클릭으로는 시연할 수 없으므로(NodeSharp.Nodes.Inject.csproj의 NR-03a 블록에 판단 근거
/// 기록), <see cref="InjectNode.TriggerAsync"/> 직접 호출을 "버튼 클릭"의 대역으로 삼아 실제
/// <see cref="FlowEngine"/> 배포·라우팅 경로로 이 완료 기준을 증명합니다(AskUserQuestion으로 확인한
/// 범위). (NR-03b) Interval 트리거 완료 기준("간격을 5초로 설정했을 때 AsyncSchedulerAdapter를 통해
/// 약 5초 주기로 발행되는지 확인")도 같은 방식(실제 <see cref="FlowEngine"/> 배포·라우팅 경로 + 짧은
/// 간격(수십 ms)·<see cref="AsyncSchedulerAdapterTests"/>와 동일한 InRange 허용 오차)으로 이 클래스에서
/// 함께 검증합니다.
/// </summary>
public class InjectNodeTests
{
    /// <summary>입력을 받아 정적 로그에 기록만 하는 테스트 전용 수신 노드 — FlowEngineRouteAsyncTests의 ReceiverNode와 동일한 패턴.</summary>
    private sealed class ReceiverNode : IFlowNode
    {
        public static readonly List<object?> Received = new();

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

    /// <summary>엔진의 <see cref="FlowEngine.RouteAsync"/>로 그대로 위임하는 테스트 전용 <see cref="INodeContext"/>(FlowEngineRouteAsyncTests.TestNodeContext와 동일한 패턴).</summary>
    private sealed class TestNodeContext : INodeContext
    {
        private readonly FlowEngine _engine;
        public TestNodeContext(FlowEngine engine) => _engine = engine;
        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct) =>
            _engine.RouteAsync(sourceNodeId, outputPort, msg, ct);
        public void SetStatus(string fill, string shape, string text) { }

        // (NR-04) INodeContext.Flow/Global 신규 멤버 — 이 파일은 이미 NodeSharp.Runtime을 참조하므로
        // 실제 구현체 ContextScope+InMemoryContextStore를 그대로 재사용(별도 스텁 불필요).
        public IContextScope Flow { get; } = new ContextScope(new InMemoryContextStore(), "flow", "test");
        public IContextScope Global { get; } = new ContextScope(new InMemoryContextStore(), "global", string.Empty);
    }

    private static FlowEngine BuildEngine(out NodeTypeRegistry registry)
    {
        registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        // 완료 기준의 핵심 전제 — NodeSharp.Nodes.Inject 어셈블리(별도 프로젝트, Contracts만 참조)가
        // NodeTypeRegistry.ScanAssembly로 실제로 스캔·등록되는지부터 확인한다(InjectNodeType.Descriptor
        // 정적 필드 관례).
        registry.ScanAssembly(typeof(InjectNodeType).Assembly);
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        return new FlowEngine(registry);
    }

    [Fact]
    public void InjectNodeType_Descriptor는_ScanAssembly로_정상_등록된다()
    {
        BuildEngine(out var registry);

        Assert.True(registry.Descriptors.ContainsKey("inject"));
        Assert.Equal("input", registry.Descriptors["inject"].Category);
        Assert.Equal(0, registry.Descriptors["inject"].DefaultInputs);
        Assert.Equal(1, registry.Descriptors["inject"].DefaultOutputs);
        // (NR-03b) PropertySchema가 "payload" 1개 → "payload"/"trigger"/"intervalSeconds" 3개로 늘어남.
        // (NR-03c) "once"/"onceDelay" 2개가 더 늘어 5개. (NR-03d) "cronExpression" 1개가 더 늘어 총
        // 6개(InjectNodeType.cs PropertySchema 참고, 실제 선언 순서 그대로 확인).
        Assert.Equal(6, registry.Descriptors["inject"].PropertySchema.Count);
        Assert.Equal("payload", registry.Descriptors["inject"].PropertySchema[0].Key);
        Assert.Equal("trigger", registry.Descriptors["inject"].PropertySchema[1].Key);
        Assert.Equal("intervalSeconds", registry.Descriptors["inject"].PropertySchema[2].Key);
        Assert.Equal("cronExpression", registry.Descriptors["inject"].PropertySchema[3].Key);
        Assert.Equal("once", registry.Descriptors["inject"].PropertySchema[4].Key);
        Assert.Equal("onceDelay", registry.Descriptors["inject"].PropertySchema[5].Key);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__TriggerAsync_1회_호출은_다음_노드에_정확히_1회_Msg를_전달한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildEngine(out _);
        var injectCfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());
        var receiverCfg = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "Inject 테스트 플로우",
            Nodes: new[] { injectCfg, receiverCfg },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var injectNode = Assert.IsType<InjectNode>(engine.Nodes["n1"]);
        var ctx = new TestNodeContext(engine);
        await injectNode.TriggerAsync("수동 발행", ctx, CancellationToken.None);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("수동 발행", ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task TriggerAsync를_3회_호출하면_다음_노드가_정확히_3회_수신한다()
    {
        // "정확히 1회"가 우연이 아니라 호출 횟수와 정확히 비례한다는 것을 함께 확인 — TriggerAsync가
        // 내부적으로 여러 번 전달하거나 누락하지 않음을 보강 검증.
        ReceiverNode.Received.Clear();
        var engine = BuildEngine(out _);
        var injectCfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());
        var receiverCfg = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "Inject 반복 테스트",
            Nodes: new[] { injectCfg, receiverCfg },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        var injectNode = Assert.IsType<InjectNode>(engine.Nodes["n1"]);
        var ctx = new TestNodeContext(engine);
        await injectNode.TriggerAsync(1, ctx, CancellationToken.None);
        await injectNode.TriggerAsync(2, ctx, CancellationToken.None);
        await injectNode.TriggerAsync(3, ctx, CancellationToken.None);

        Assert.Equal(3, ReceiverNode.Received.Count);
        Assert.Equal(new object?[] { 1, 2, 3 }, ReceiverNode.Received);
    }

    [Fact]
    public void InjectNode는_입력_포트가_0개이고_출력_포트가_1개다()
    {
        var node = new InjectNode { Id = "n1" };

        Assert.Empty(node.InputPorts);
        Assert.Single(node.OutputPorts);
    }

    [Fact]
    public void InjectNodeType_Factory는_trigger_intervalSeconds_payload_속성을_읽어_InjectNode에_채운다()
    {
        var cfg = new NodeConfig(
            "n1", "inject", "주기 트리거", "f1",
            new Dictionary<string, object?> { ["trigger"] = "interval", ["intervalSeconds"] = 5.0, ["payload"] = "tick" });

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.Equal("interval", node.TriggerMode);
        Assert.Equal(5.0, node.IntervalSeconds);
        Assert.Equal("tick", node.DefaultPayload);
    }

    [Fact]
    public void InjectNodeType_Factory는_trigger_속성이_없으면_manual을_기본값으로_쓴다()
    {
        var cfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.Equal("manual", node.TriggerMode);
        Assert.Equal(0.0, node.IntervalSeconds);
        Assert.Null(node.DefaultPayload);
    }

    /// <summary>
    /// (NR-03b) Interval/OnCloseAsync 테스트 전용 — <see cref="FlowEngine"/>에 "n1"용 <c>NodeConfig</c>
    /// 없이 Wire(n1→n2)와 receiver(n2)만 배포한다(<see cref="FlowEngine.RouteAsync"/>는 소스 노드가 실제로
    /// <see cref="FlowEngine.Nodes"/>에 등록돼 있을 필요가 없고 Wire의 SourceNodeId만 일치하면 됨 — 기존
    /// FlowEngineRouteAsyncTests의 전제와 동일). 이렇게 하면 <see cref="InjectNode"/>를 Factory 경로가
    /// 아니라 이 테스트가 직접 생성해 <see cref="InjectNode.Scheduler"/>에 격리된 테스트 전용
    /// <see cref="AsyncScheduler"/>를 주입할 수 있다(<see cref="AsyncSchedulerAdapterTests"/>와 동일한
    /// 원칙 — 앱 전체 공유 싱글턴과 예약이 섞이지 않게 함).
    /// </summary>
    private static FlowEngine BuildWireOnlyEngine()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("receiver", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(ReceiverNode));
        var engine = new FlowEngine(registry);
        var flow = new FlowDefinition(
            Id: "f1", Name: "Interval 테스트 플로우",
            Nodes: new[] { new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>()) },
            Wires: new[] { new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 0) });
        engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None).GetAwaiter().GetResult();
        return engine;
    }

    [Fact]
    public async Task 완료_기준_직접_검증__Interval_모드는_설정한_간격마다_반복_발행한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            TriggerMode = "interval",
            IntervalSeconds = 0.02,
            DefaultPayload = "auto",
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(160);
        await injectNode.OnCloseAsync(ctx);   // 다음 테스트로 스케줄이 새지 않도록 정리

        Assert.True(ReceiverNode.Received.Count >= 3,
            $"160ms 동안 20ms 간격이면 최소 3번은 발행돼야 하는데 {ReceiverNode.Received.Count}번 발행됨");
        Assert.All(ReceiverNode.Received, payload => Assert.Equal("auto", payload));
    }

    [Fact]
    public async Task 완료_기준_직접_검증__OnCloseAsync_이후로는_더_이상_발행되지_않는다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            TriggerMode = "interval",
            IntervalSeconds = 0.02,
            DefaultPayload = "auto",
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(80);
        await injectNode.OnCloseAsync(ctx);
        var countAtClose = ReceiverNode.Received.Count;
        await Task.Delay(150);

        Assert.InRange(ReceiverNode.Received.Count, countAtClose, countAtClose + 1);   // 진행 중이던 1회는 예외로 허용
    }

    [Fact]
    public async Task IntervalSeconds가_0이면_OnStartAsync가_아무것도_예약하지_않는다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            TriggerMode = "interval",
            IntervalSeconds = 0,   // 0 이하 — 자동 발행 시작 안 함
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(80);
        var ex = await Record.ExceptionAsync(() => injectNode.OnCloseAsync(ctx));   // 예약이 없어도 안전해야 함

        Assert.Empty(ReceiverNode.Received);
        Assert.Null(ex);
    }

    [Fact]
    public void InjectNodeType_Factory는_once_onceDelay_속성을_읽어_InjectNode에_채운다()
    {
        var cfg = new NodeConfig(
            "n1", "inject", "기동 시 트리거", "f1",
            new Dictionary<string, object?> { ["once"] = true, ["onceDelay"] = 2.0 });

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.True(node.Once);
        Assert.Equal(2.0, node.OnceDelaySeconds);
    }

    [Fact]
    public void InjectNodeType_Factory는_once_속성이_없으면_false와_기본_지연_0_1초를_쓴다()
    {
        var cfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.False(node.Once);
        Assert.Equal(0.1, node.OnceDelaySeconds);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__Once가_true면_배포_후_onceDelay_뒤에_정확히_1회만_발행한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            Once = true,
            // (★ 버그 수정, 2026-08-12) 이전엔 OnceDelaySeconds=0.02(20ms)와 아래 "onceDelay 전" 확인의
            // Task.Delay(20)이 같은 20ms라 Task.Delay의 타이머 해상도(Windows 기준 대략 15ms 단위)
            // 안에서 두 지연이 서로 앞서거나 뒤서는 순서를 보장할 수 없는 경쟁 상태(race condition)였다
            // — 사용자가 실제 dotnet test 실행에서 "onceDelay 전" 확인이 이미 발행된 상태로 실패하는
            // 것을 보고해 발견. OnceDelaySeconds를 0.12(120ms)로 늘려 아래 "onceDelay 전" 확인의
            // Task.Delay(20)과 6배 이상 여유를 두고, "onceDelay 이후" 확인도 총 250ms를 기다려 120ms와
            // 충분한 여유(약 2배)를 둠 — 값 자체(20ms)가 아니라 두 지연 사이의 상대적 여유가 핵심이라
            // 완료 기준("정확히 1회만 발행")의 검증 내용은 그대로 유지된다.
            OnceDelaySeconds = 0.12,
            DefaultPayload = "boot",
            // TriggerMode는 기본값 "manual" — interval을 켜지 않았으므로 once 1회 이후 반복되지 않아야 함.
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(20);   // onceDelay(120ms) 전 — 아직 발행되면 안 됨(6배 이상 여유)
        Assert.Empty(ReceiverNode.Received);

        await Task.Delay(230);   // 누적 250ms — onceDelay(120ms) 이후 충분히 대기(약 2배 여유)
        await injectNode.OnCloseAsync(ctx);

        Assert.Single(ReceiverNode.Received);
        Assert.Equal("boot", ReceiverNode.Received[0]);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__재배포_시에도_Once는_인스턴스마다_정확히_1회만_발행한다()
    {
        // "재배포 시에도 다시 1회만 발행되는지" — 새 InjectNode 인스턴스를 2번 만들어 각각
        // OnStartAsync/OnCloseAsync 주기를 거치는 것으로 재배포를 재현(FlowEngine.DeployAsync가
        // 재배포마다 새 인스턴스를 만드는 것과 동일한 전제, RT-03 주석 참고).
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);

        var firstDeploy = new InjectNode { Id = "n1", Once = true, OnceDelaySeconds = 0.02, DefaultPayload = "1차" };
        await firstDeploy.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(60);
        await firstDeploy.OnCloseAsync(ctx);

        var secondDeploy = new InjectNode { Id = "n1", Once = true, OnceDelaySeconds = 0.02, DefaultPayload = "2차" };
        await secondDeploy.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(60);
        await secondDeploy.OnCloseAsync(ctx);

        Assert.Equal(new object?[] { "1차", "2차" }, ReceiverNode.Received);
    }

    [Fact]
    public async Task Once와_Interval을_동시에_켜면_1회_발행_후_반복도_이어서_시작한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            Once = true,
            OnceDelaySeconds = 0.02,
            TriggerMode = "interval",
            IntervalSeconds = 0.02,
            DefaultPayload = "auto",
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(180);   // once 1회 + interval 반복 여러 회가 일어날 시간
        await injectNode.OnCloseAsync(ctx);

        Assert.True(ReceiverNode.Received.Count >= 2,
            $"once 1회 + interval 반복이 함께 동작해야 하는데 {ReceiverNode.Received.Count}번만 발행됨");
    }

    [Fact]
    public async Task OnCloseAsync가_onceDelay_경과_전에_호출되면_대기_중인_1회_발행을_취소한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            Once = true,
            OnceDelaySeconds = 1.0,   // 충분히 긴 지연 — 아래에서 delay가 지나기 전에 Close
            DefaultPayload = "boot",
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await injectNode.OnCloseAsync(ctx);   // onceDelay(1초)가 지나기 전에 즉시 닫음
        await Task.Delay(1200);   // onceDelay가 지났어도 취소됐으니 발행되면 안 됨

        Assert.Empty(ReceiverNode.Received);
    }

    [Fact]
    public void InjectNodeType_Factory는_cronExpression_속성을_읽어_InjectNode에_채운다()
    {
        var cfg = new NodeConfig(
            "n1", "inject", "cron 트리거", "f1",
            new Dictionary<string, object?> { ["trigger"] = "cron", ["cronExpression"] = "* * * * *" });

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.Equal("cron", node.TriggerMode);
        Assert.Equal("* * * * *", node.CronExpressionText);
    }

    [Fact]
    public void InjectNodeType_Factory는_cronExpression_속성이_없으면_빈_문자열을_쓴다()
    {
        var cfg = new NodeConfig("n1", "inject", "수동 트리거", "f1", new Dictionary<string, object?>());

        var node = Assert.IsType<InjectNode>(InjectNodeType.Descriptor.Factory(cfg));

        Assert.Equal(string.Empty, node.CronExpressionText);
    }

    [Fact]
    public async Task 완료_기준_직접_검증__Cron_모드는_표현식에_맞는_시각마다_발행한다()
    {
        // AsyncSchedulerAdapterTests.ScheduleCron은_조건에_맞는_순간에만_콜백을_호출한다()와 동일한
        // 원칙 — "*"(모든 초에 일치)로 어댑터의 1초 폴링 특성상 1.2초 정도면 최소 1번은 호출돼야 한다.
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            TriggerMode = "cron",
            CronExpressionText = "* * * * * *",   // 6필드, 매초 일치
            DefaultPayload = "tick",
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(1200);
        await injectNode.OnCloseAsync(ctx);

        Assert.True(ReceiverNode.Received.Count >= 1,
            $"1.2초 동안 매초 일치하는 cron이 한 번도 호출되지 않음(발행 {ReceiverNode.Received.Count}회)");
    }

    [Fact]
    public async Task OnCloseAsync는_Cron_예약도_함께_해제한다()
    {
        ReceiverNode.Received.Clear();
        var engine = BuildWireOnlyEngine();
        var ctx = new TestNodeContext(engine);
        var injectNode = new InjectNode
        {
            Id = "n1",
            TriggerMode = "cron",
            CronExpressionText = "* * * * * *",
            DefaultPayload = "tick",
            Scheduler = new AsyncSchedulerAdapter(new AsyncScheduler()),
        };

        await injectNode.OnStartAsync(ctx, CancellationToken.None);
        await Task.Delay(1200);
        await injectNode.OnCloseAsync(ctx);
        var countAtClose = ReceiverNode.Received.Count;
        await Task.Delay(1200);

        Assert.Equal(countAtClose, ReceiverNode.Received.Count);   // Close 이후로는 더 이상 늘지 않음
    }

    [Fact]
    public async Task 완료_기준_직접_검증__잘못된_cron_표현식은_배포_시_이_노드만_실패_처리된다()
    {
        // "잘못된 표현식은 검증 오류로 표시되는지 확인" — WPF 편집 UI가 아직 없어(개발 지침 참고),
        // FlowEngine.DeployAsync(RT-02b)의 기존 노드별 예외 격리 메커니즘으로 대신 검증한다: 이 노드만
        // FailedNodeIds에 기록되고 나머지 배포·다른 노드 동작에는 영향이 없어야 한다.
        var engine = BuildEngine(out _);
        var injectCfg = new NodeConfig(
            "n1", "inject", "잘못된 cron", "f1",
            new Dictionary<string, object?> { ["trigger"] = "cron", ["cronExpression"] = "이건 cron이 아님" });
        var receiverCfg = new NodeConfig("n2", "receiver", "수신", "f1", new Dictionary<string, object?>());
        var flow = new FlowDefinition(
            Id: "f1", Name: "잘못된 cron 테스트",
            Nodes: new[] { injectCfg, receiverCfg },
            Wires: Array.Empty<Wire>());

        var ex = await Record.ExceptionAsync(() => engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None));

        Assert.Null(ex);   // 배포 자체는 예외 없이 성공(RT-02b 원칙 — 노드 하나의 문제가 전체를 막지 않음)
        Assert.Contains("n1", engine.FailedNodeIds);
        Assert.True(engine.Nodes.ContainsKey("n2"));   // n2(receiver)는 영향받지 않고 정상 배포됨
    }
}
