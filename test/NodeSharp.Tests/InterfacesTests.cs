using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="IFlowNode"/>/<see cref="ISharedServiceNode"/>/<see cref="IEditorCommand"/>/
/// <see cref="INodeContext"/>(CT-04a, 02번 설계 문서 2번 탭 카드 1·6·9)에 대한 단위 테스트입니다.
/// 인터페이스 자체는 동작이 없으므로, 여기서는 (1) 최소 스텁 구현이 실제로 컴파일·동작하는지,
/// (2) <see cref="INodeContext"/>의 기본 인터페이스 멤버(<c>SetStatus(NodeStatusLevel, ...)</c>)가
/// 문자열 오버로드로 올바르게 위임되는지, (3) (NR-11) <c>INodeContext.Debug</c>가 인자를 그대로
/// 전달하는지를 확인합니다.
/// </summary>
public class InterfacesTests
{
    /// <summary>테스트 전용 <see cref="INodeContext"/> 스텁 — RouteAsync/SetStatus 호출 여부와 인자를 기록합니다.</summary>
    private sealed class FakeNodeContext : INodeContext
    {
        public (string SourceNodeId, int OutputPort, Msg Msg)? LastRoute { get; private set; }
        public (string Fill, string Shape, string Text)? LastStatus { get; private set; }
        public (string NodeName, string MsgJson)? LastDebug { get; private set; }

        // (NR-04) INodeContext.Flow/Global 신규 멤버 — 이 파일은 인터페이스 자체(Contracts)만
        // 다루는 최소 스텁 취지라, Runtime의 ContextScope를 끌어오지 않고 Dictionary 기반의
        // 자체 IContextScope 스텁(StubContextScope)으로 채운다.
        public IContextScope Flow { get; } = new StubContextScope();
        public IContextScope Global { get; } = new StubContextScope();

        public Task RouteAsync(string sourceNodeId, int outputPort, Msg msg, CancellationToken ct)
        {
            LastRoute = (sourceNodeId, outputPort, msg);
            return Task.CompletedTask;
        }

        public void SetStatus(string fill, string shape, string text) => LastStatus = (fill, shape, text);

        // (NR-11) INodeContext.Debug 신규 멤버 — 실제 이벤트 발행 없이 호출 여부·인자만 기록.
        public void Debug(string nodeName, string msgJson) => LastDebug = (nodeName, msgJson);
    }

    /// <summary>(NR-04) <see cref="IContextScope"/> 최소 스텁 — 실제 Context 저장소 없이 Dictionary 하나로 Get/Set/Keys를 그대로 구현.</summary>
    private sealed class StubContextScope : IContextScope
    {
        private readonly Dictionary<string, object?> _data = new();

        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) && v is T t ? t : default;

        public void Set(string key, object? value) => _data[key] = value;

        public IEnumerable<string> Keys() => _data.Keys;
    }

    /// <summary>테스트 전용 <see cref="IFlowNode"/> 스텁 — 입력을 0번 출력 포트로 그대로 전달하는 패스스루 노드.</summary>
    private sealed class PassThroughNode : IFlowNode
    {
        public string Id { get; init; } = "n1";
        public string Type => "pass-through";
        public string Name { get; set; } = "패스스루";
        public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };
        public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) =>
            ctx.RouteAsync(Id, 0, msg, ct);

        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    /// <summary>테스트 전용 <see cref="ISharedServiceNode"/> 스텁 — Start/Stop 호출 여부만 기록.</summary>
    private sealed class FakeSharedService : ISharedServiceNode
    {
        public string Id { get; init; } = "svc-1";
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken ct) { Started = true; return Task.CompletedTask; }
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
    }

    /// <summary>테스트 전용 <see cref="IEditorCommand"/> 스텁 — Do/Undo로 값 하나를 토글.</summary>
    private sealed class ToggleCommand : IEditorCommand
    {
        public bool Value { get; private set; }
        public string Description => "값 토글";
        public void Do() => Value = true;
        public void Undo() => Value = false;
    }

    [Fact]
    public async Task IFlowNode_OnInputAsync는_ctx_RouteAsync를_같은_노드ID와_0번_포트로_호출한다()
    {
        var node = new PassThroughNode();
        var ctx = new FakeNodeContext();
        var msg = new Msg { Payload = 42 };

        await node.OnInputAsync(msg, ctx, CancellationToken.None);

        Assert.NotNull(ctx.LastRoute);
        Assert.Equal(("n1", 0, msg), ctx.LastRoute);
    }

    [Fact]
    public void INodeContext_SetStatus_NodeStatusLevel_오버로드는_문자열_오버로드로_위임된다()
    {
        INodeContext ctx = new FakeNodeContext();

        ctx.SetStatus(NodeStatusLevel.Green, "dot", "연결됨");

        var fake = (FakeNodeContext)ctx;
        Assert.Equal(("green", "dot", "연결됨"), fake.LastStatus);
    }

    [Fact]
    public void INodeContext_Debug는_nodeName과_msgJson을_그대로_전달한다()
    {
        // (NR-11) 인터페이스 계약 자체(어떤 인자로 호출되는지)만 검증 — 실제 DebugMessageEvent 발행은
        // NodeContext(Runtime)의 몫이라 이 파일(Contracts 전용) 범위 밖(DebugNodeTests가 실제 발행까지 검증).
        var ctx = new FakeNodeContext();

        ctx.Debug("디버그", "{\"payload\":42}");

        Assert.Equal(("디버그", "{\"payload\":42}"), ctx.LastDebug);
    }

    [Fact]
    public async Task ISharedServiceNode_StartAsync와_StopAsync가_각각_한_번씩_호출되면_플래그가_모두_true다()
    {
        var service = new FakeSharedService();

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync();

        Assert.True(service.Started);
        Assert.True(service.Stopped);
    }

    [Fact]
    public void IEditorCommand_Do와_Undo는_서로_반대_상태로_되돌린다()
    {
        var cmd = new ToggleCommand();

        cmd.Do();
        Assert.True(cmd.Value);

        cmd.Undo();
        Assert.False(cmd.Value);
        Assert.Equal("값 토글", cmd.Description);
    }
}
