namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 컨텍스트 스코프 계약
/// 역활 및 기능 : 특정 Context 스코프(Flow/Global 등) 하나에 대한 Get/Set/Keys 계약
///
/// <see cref="INodeContext"/>가 노출하는 Flow/Global 등 Context 스코프 하나를 다루는 계약입니다
/// (NR-04, Switch 노드의 TypedValue FlowContext/GlobalContext Source 지원을 위해 신설). 실제 구현체인
/// <c>NodeSharp.Runtime.ContextScope</c>는 <c>IContextStore</c>(RT-09a)를 감싸는 구조체인데, 그 구조체가
/// Runtime 소속이라 Contracts에 그대로 노출하면 Runtime↔Contracts 순환 참조가 됩니다 — <see cref="IFlowNode"/>가
/// <see cref="INodeContext"/> 인터페이스로 <c>NodeContext</c>(Runtime 구현체)를 감췄던 것과 동일한 이유로,
/// 이 인터페이스가 그 경계를 끊습니다.
/// </summary>
/// <remarks>
/// <c>nodes\*</c> 코어 노드 플러그인(Contracts+Util만 참조)이 <see cref="INodeContext.Flow"/>/
/// <see cref="INodeContext.Global"/>을 통해 Context 값을 읽고 쓸 수 있게 하는 것이 이 인터페이스의
/// 유일한 존재 이유입니다 — 그 전까지는 Flow/Global 접근자가 구현체 <c>NodeContext</c>(Runtime)에만
/// 있어 Contracts+Util만 참조하는 노드 플러그인에서는 쓸 방법이 없었습니다.
/// </remarks>
/// <example>
/// <code>
/// // SwitchNode(nodes\NodeSharp.Nodes.Switch, Contracts+Util만 참조)가 INodeContext만으로 Flow Context를 읽는 예
/// public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
/// {
///     int? lastAlarmLevel = ctx.Flow.Get&lt;int&gt;("lastAlarmLevel");
///     ctx.Global.Set("lastSwitchNodeId", Id);
///     IEnumerable&lt;string&gt; flowKeys = ctx.Flow.Keys();
///     return Task.CompletedTask;
/// }
/// </code>
/// </example>
public interface IContextScope
{
    /// <summary>이 스코프 안에서 <paramref name="key"/> 값을 읽습니다. 값이 없으면 <c>default(T)</c>를 반환합니다.</summary>
    T? Get<T>(string key);

    /// <summary>이 스코프 안에 <paramref name="key"/> 값을 저장(또는 덮어쓰기)합니다.</summary>
    void Set(string key, object? value);

    /// <summary>이 스코프 안에 저장된 모든 키 이름을 열거합니다.</summary>
    IEnumerable<string> Keys();
}
