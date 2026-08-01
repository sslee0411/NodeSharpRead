namespace NodeSharp.Runtime;

/// <summary>
/// 노드 하나가 동시에 몇 번까지 실행될 수 있는지를 제한하는 "통행 허가증" 역할의
/// <see cref="SemaphoreSlim"/>(동시 진입 개수를 세는 대기열 장치)을 관리합니다(05번 탭 카드3 원본
/// 그대로 별도 파일로 분리). <see cref="FlowEngine"/>은 <c>DispatchOneAsync</c>에서 대상 노드에게
/// 메시지를 넘기기 전에 이 게이트를 통과시킵니다. HTTP 요청이나 PLC 장비 통신처럼 응답을 오래 기다릴
/// 수 있는 노드가 동시에 너무 많이 실행되면, 시스템의 실행 대기열(스레드풀)이 가득 차거나 노드 내부
/// 값(커넥션·카운터 등)이 동시에 바뀌면서 꼬이는 문제가 생길 수 있습니다. 이 게이트는 그런 문제를
/// 막아줍니다.
/// 설계 근거: 02번 문서 5번 탭 카드 3.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>키는 <c>NodeConfig.Id</c></b> — 카드3 원본은 <c>_gates[node.Id]</c>(<c>IFlowNode.Id</c>)로
/// 키를 삼지만, <c>IFlowNode.Id</c>는 아직 <c>NodeConfig.Id</c>와 동기화되지 않습니다(<c>RG-01</c> 대기,
/// <c>RT-01a</c> Ver History 참고 — <c>Activator.CreateInstance</c>로 만든 노드는 매번 새 Guid를 자체
/// Id로 가짐). <see cref="FlowEngine"/>의 다른 모든 노드 조회(<see cref="FlowEngine.Nodes"/>,
/// <c>Wire.TargetNodeId</c> 매칭 등)가 이미 <c>NodeConfig.Id</c>를 안정적 식별자로 쓰고 있으므로
/// (<c>RT-01b</c> 결정), 이 게이트도 동일한 <c>NodeConfig.Id</c>를 키로 받습니다.</item>
/// <item><b>허용치는 최소 1로 맞춤</b> — <see cref="SemaphoreSlim"/>은 0 이하의 허용치로는 만들 수 없습니다.
/// 설정 오류(<c>MaxConcurrency</c>가 0 이하로 저장된 경우)로 배포 전체가 멈추지 않도록,
/// <see cref="GetGate"/>는 항상 <c>Math.Max(1, maxConcurrency)</c>로 값을 1 이상으로 맞춰줍니다.</item>
/// <item><b>재배포 시 갱신</b>은 이 클래스가 아니라 <see cref="FlowEngine"/>의 책임입니다 — 노드가
/// 닫힐 때 <see cref="RemoveGate"/>로 게이트를 제거해, 다음 배포에서 같은 Id로 노드가 다시 만들어지면
/// (<c>MaxConcurrency</c> 설정이 바뀌었을 수 있으므로) 새 게이트가 최신 설정으로 다시 생성되게 합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 동시 실행 2개까지 허용하는 게이트 — 3번째 요청은 앞선 요청이 끝날 때까지 대기
/// var gate = new NodeExecutionGate();
/// var sem = gate.GetGate("n1", maxConcurrency: 2);
/// await sem.WaitAsync();
/// try { /* 실제 작업 */ }
/// finally { sem.Release(); }
///
/// // 2) 같은 Id로 다시 요청하면 항상 같은 인스턴스를 돌려줌(두 번째 인자는 최초 생성 시에만 쓰임)
/// var same = gate.GetGate("n1", maxConcurrency: 99);   // 이미 만들어진 2짜리 세마포어를 그대로 반환
/// bool isSame = ReferenceEquals(sem, same);              // true
///
/// // 3) 노드 재배포로 설정이 바뀌면 게이트를 제거해 다음 GetGate 호출이 새 값으로 재생성하게 한다
/// gate.RemoveGate("n1");
/// var refreshed = gate.GetGate("n1", maxConcurrency: 5);   // 이번엔 실제로 5짜리로 새로 생성됨
/// </code>
/// </example>
public sealed class NodeExecutionGate
{
    private readonly Dictionary<string, SemaphoreSlim> _gates = new();

    /// <summary>
    /// <paramref name="nodeConfigId"/>에 대응하는 <see cref="SemaphoreSlim"/>을 반환합니다. 처음 호출되면
    /// <paramref name="maxConcurrency"/>(최소 1로 보정)로 새로 만들고, 이미 있으면 기존 인스턴스를 그대로
    /// 반환합니다(이 경우 <paramref name="maxConcurrency"/>는 무시됨 — 값을 바꾸려면 <see cref="RemoveGate"/>
    /// 후 다시 호출).
    /// </summary>
    public SemaphoreSlim GetGate(string nodeConfigId, int maxConcurrency)
    {
        if (_gates.TryGetValue(nodeConfigId, out var gate)) return gate;

        var safeMax = Math.Max(1, maxConcurrency);
        gate = new SemaphoreSlim(safeMax, safeMax);
        _gates[nodeConfigId] = gate;
        return gate;
    }

    /// <summary>
    /// <paramref name="nodeConfigId"/>의 게이트를 제거하고 <see cref="SemaphoreSlim"/>을 <c>Dispose</c>합니다.
    /// 해당 노드가 재배포로 닫힐 때 <see cref="FlowEngine"/>이 호출해, 다음 배포에서 같은 Id로 노드가 다시
    /// 만들어지면 최신 <c>MaxConcurrency</c> 설정으로 새 게이트가 생성되게 합니다. 존재하지 않는 Id를
    /// 전달해도 예외 없이 아무 일도 하지 않습니다.
    /// </summary>
    public void RemoveGate(string nodeConfigId)
    {
        if (_gates.Remove(nodeConfigId, out var gate))
        {
            gate.Dispose();
        }
    }
}
