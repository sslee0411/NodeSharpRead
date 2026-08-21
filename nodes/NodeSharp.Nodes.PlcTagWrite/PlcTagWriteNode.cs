using System.Collections.Concurrent;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.PlcTagWrite;

/// <summary>
/// Class명 : PLC 태그 쓰기 노드
/// 역활 및 기능 : 구조 설정 트리의 태그(TagNode) 하나를 TagRef로 참조해 값을 쓰는 예시 노드 — 범위 검사와
/// 동시 쓰기 락(ED-D06a)만 증명하는 안전장치 동작을 합니다.
///
/// (ED-D06a) 02번 설계문서의 "PLC 쓰기 노드에 범위 검사 + 쓰기 락(동시 쓰기 방지)" 완료 기준을
/// 그대로 구현했습니다. <see cref="PlcTagReadNode"/>(ED-D04)와 동일한 범위 축소 근거로 — TagId가
/// 가리키는 태그를 실제 PLC(<c>IProtocolDriver.WriteAsync</c>)로 연결하려면 TagId→PLC 연결 정보를
/// 해석하는 <c>IStructureService</c>가 필요한데 아직 어디에도 구현돼 있지 않습니다 — 이 노드는 실제
/// PLC 통신을 직접 수행하지 않고, 그 자리에 <see cref="WriteAction"/>(기본값 자리표시자, 테스트가
/// 주입해 관찰) 훅을 둡니다. 이 Step의 완료 기준("범위를 벗어난 값 쓰기는 거부", "같은 태그에 대한
/// 동시 쓰기 요청 중 하나는 락으로 대기")은 실제 PLC 연결 여부와 무관하게 검증 가능합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><see cref="WriteAction"/> 테스트 주입점</b>: <see cref="InjectNode.Scheduler"/>(NR-03b)와
/// 동일한 관례로, 실제 쓰기 동작을 속성으로 노출해 테스트가 지연·기록 로직을 주입할 수 있게 했습니다
/// — 프로덕션 기본값(<c>null</c>)은 아무 것도 하지 않는 자리표시자입니다.</item>
/// <item><b>동시 쓰기 락은 TagId 기준(인스턴스 기준이 아님)</b>: <see cref="_tagLocks"/>는
/// <c>static</c> 딕셔너리라, 서로 다른 <see cref="PlcTagWriteNode"/> 인스턴스라도 같은
/// <see cref="TagId"/>를 가리키면 같은 락을 공유합니다 — 완료 기준이 "같은 태그"를 기준으로
/// 요구하기 때문입니다(같은 노드 인스턴스로의 동시 호출만 막는 것은 요구사항에 못 미침).</item>
/// <item><b>범위 밖 값은 락을 잡지 않고 즉시 거부</b>: <see cref="MinValue"/>/<see cref="MaxValue"/>
/// 검사가 락 획득보다 먼저 일어납니다 — 거부될 값 때문에 다른 정상 쓰기가 불필요하게 대기하지
/// 않도록 하기 위함입니다.</item>
/// </list>
/// </remarks>
public sealed class PlcTagWriteNode : IFlowNode
{
    /// <summary>(ED-D06a) 같은 <see cref="TagId"/>를 가리키는 모든 노드 인스턴스가 공유하는 태그별 쓰기 락 — 클래스 remarks 참고.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _tagLocks = new();

    /// <inheritdoc />
    public string Id { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Type => "plcTagWrite";

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <summary>입력 1개 — msg.Payload를 쓸 값으로 받습니다.</summary>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <summary>출력 1개 — 쓰기가 실제로 수락된 경우에만(범위 검사 통과) 그 값을 그대로 내보냅니다.</summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>(ED-D06a) 구조 설정 트리에서 선택한 태그의 고유 Id — <see cref="PlcTagReadNode"/>와 동일한 TagRef 연동 방식입니다.</summary>
    public string TagId { get; init; } = string.Empty;

    /// <summary>(ED-D06a) 이 값보다 작은 쓰기는 거부됩니다. <c>null</c>이면 하한 검사를 하지 않습니다.</summary>
    public double? MinValue { get; init; }

    /// <summary>(ED-D06a) 이 값보다 큰 쓰기는 거부됩니다. <c>null</c>이면 상한 검사를 하지 않습니다.</summary>
    public double? MaxValue { get; init; }

    /// <summary>
    /// (ED-D06a) 실제 PLC 쓰기 동작 — 기본값(<c>null</c>)은 아무 것도 하지 않는 자리표시자입니다
    /// (클래스 문서 참고, TagId→PLC 연결 정보를 해석하는 IStructureService가 아직 없어 실제 통신은
    /// 후속 Step으로 미룹니다). 테스트는 이 속성에 지연·기록 로직을 주입해 <see cref="_tagLocks"/>가
    /// 실제로 동시 쓰기를 직렬화하는지 검증합니다.
    /// </summary>
    public Func<double, CancellationToken, Task>? WriteAction { get; set; }

    /// <summary>연결 초기화가 필요 없어 아무 것도 하지 않습니다.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// <paramref name="msg"/>.Payload를 숫자로 해석해 범위를 검사하고(벗어나면 즉시 거부, 락을 잡지
    /// 않음), <see cref="TagId"/>별 락을 잡은 뒤 <see cref="WriteAction"/>(있으면)을 호출하고 0번
    /// 출력 포트로 그 값을 전달합니다. TagId가 비어 있거나 Payload가 숫자로 해석되지 않으면 아무
    /// 것도 하지 않습니다(조용히 무시 — Required 검증은 별도 범위).
    /// </summary>
    public async Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(TagId) || !TryParseValue(msg.Payload, out var value))
        {
            return;
        }

        if ((MinValue.HasValue && value < MinValue.Value) || (MaxValue.HasValue && value > MaxValue.Value))
        {
            return; // 범위 밖 — 락을 잡지 않고 즉시 거부(클래스 remarks 참고).
        }

        var gate = _tagLocks.GetOrAdd(TagId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (WriteAction is not null)
            {
                await WriteAction(value, ct).ConfigureAwait(false);
            }

            await ctx.RouteAsync(Id, outputPort: 0, new Msg { Payload = value }, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>정리할 연결·구독이 없어 아무 것도 하지 않습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;

    /// <summary><paramref name="payload"/>를 숫자(double)로 해석합니다 — double/int/float/숫자 문자열을 지원, 그 외는 실패로 처리합니다.</summary>
    private static bool TryParseValue(object? payload, out double value)
    {
        switch (payload)
        {
            case double d:
                value = d;
                return true;
            case int i:
                value = i;
                return true;
            case float f:
                value = f;
                return true;
            case string s when double.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
