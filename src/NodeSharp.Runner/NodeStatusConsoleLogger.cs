using NodeSharp.Contracts.Events;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 노드 상태 콘솔 로거
/// 역활 및 기능 : IEventBus에 발행되는 NodeStatusEvent를 콘솔 표준 출력으로 그대로 찍어주는 얇은 구독자
///
/// (RN-02) <c>IEventBus</c>에 발행되는 <see cref="NodeStatusEvent"/>를 콘솔 표준 출력으로 그대로
/// 찍어주는 얇은 구독자입니다. <c>NodeContext.SetStatus</c>(RT-09b)가 이미 <see cref="NodeStatusEvent"/>를
/// 발행하고 있으므로, 이 클래스는 그 이벤트를 받아 사람이 읽을 수 있는 한 줄로 <c>Console.WriteLine</c>만
/// 합니다 — Node-RED 디버그 사이드바의 "노드 상태 점" 표시를 헤드리스 콘솔 버전으로 대체한 최소 형태입니다
/// (실제 Editor 사이드바 연동은 Phase 5~8, SignalR 스트리밍은 별도 Step).
/// </summary>
/// <example>
/// <code>
/// var engine = new FlowEngine(registry);
/// new NodeStatusConsoleLogger().Subscribe(engine.EventBus);
/// await engine.DeployAsync(flow, DeployMode.Full, ct);
/// // 배포된 노드가 ctx.SetStatus(...)를 호출할 때마다 콘솔에 한 줄씩 출력됨
/// </code>
/// </example>
public sealed class NodeStatusConsoleLogger
{
    /// <summary>
    /// <paramref name="eventBus"/>에 발행되는 <see cref="NodeStatusEvent"/>를 구독해 콘솔에 출력합니다.
    /// 구독을 끊고 싶으면 반환된 <see cref="IDisposable"/>을 <c>Dispose()</c>하세요.
    /// </summary>
    public IDisposable Subscribe(IEventBus eventBus) =>
        eventBus.Subscribe<NodeStatusEvent>(e =>
            Console.WriteLine($"[{e.At:HH:mm:ss}] {e.NodeId} {e.Fill}/{e.Shape} — {e.Text}"));
}
