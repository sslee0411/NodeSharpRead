using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runtime;

/// <summary>
/// Class명 : 누락 노드(자리표시자)
/// 역활 및 기능 : 존재하지 않는 노드 타입 자리에 대신 배포되는 자리표시자 노드
///
/// ★ 한줄 요약: <b>"노드 타입을 찾을 수 없을 때"만</b> 쓰는 자리표시자입니다 — JSON 파싱 실패·빈 값이나
/// <c>OnStartAsync</c> 실행 중 다른 이유의 기동 실패(<c>RT-02b</c>가 별도 처리)와는 무관합니다.
/// 존재하지 않는(또는 삭제된 플러그인의) 노드 타입 자리에 대신 배포되는 자리표시자입니다.
/// Node-RED Editor의 "missing" 노드와 동일한 개념 — 캔버스에는 빨간 테두리 + "⚠ 알 수 없는 타입:
/// {Type}"로 표시되어, 사용자가 플러그인을 다시 설치하거나 노드를 삭제하도록 유도합니다.
/// 설계 근거: 02번 문서 2번 탭 카드 4(v1.9 결함의 정식 반영), 3번 탭 카드 6.
/// </summary>
/// <remarks>
/// <see cref="FlowEngine.DeployAsync"/>가 <see cref="FlowEngine.CreateInstance"/> 실패(등록되지 않은
/// 타입)를 잡아 이 노드로 대체합니다 — 배포 전체를 실패시키지 않고 해당 노드 하나만 자리표시자로
/// 남긴 채 나머지 노드는 정상 배포됩니다. 입력을 받아도 그냥 버리고 출력도 하지 않으며,
/// <b><see cref="OnStartAsync"/>는 <see cref="FlowEngine.DeployAsync"/>가 아예 호출을 건너뜁니다</b>
/// (2번 탭 카드4 원본 주석 — "자리표시자는 OnStartAsync 자체가 없음"). 인터페이스 계약을 만족시키기
/// 위해 구현은 남겨두지만 실제로는 호출되지 않습니다.
/// </remarks>
/// <example>
/// <code>
/// // FlowEngine.DeployAsync 내부(개념 설명용) — CreateInstance가 InvalidOperationException을 던지면:
/// var node = new MissingNode(cfg.Id, cfg.Type);
/// // 캔버스에는 "⚠ 알 수 없는 타입: mqtt-in-legacy" 배지로 표시(Editor 구현은 별도 Step)
/// </code>
/// </example>
public sealed class MissingNode : IFlowNode
{
    /// <summary>등록되지 않은 원본 <see cref="NodeConfig.Id"/>와 <see cref="NodeConfig.Type"/>으로 자리표시자를 만듭니다.</summary>
    public MissingNode(string id, string type)
    {
        Id = id;
        Type = type;
        Name = $"⚠ 알 수 없는 타입: {type}";
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>찾을 수 없었던 원본 노드 타입 이름 — 사용자가 어떤 플러그인을 다시 설치해야 하는지 알 수 있게 보존합니다.</summary>
    public string Type { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();

    /// <inheritdoc/>
    public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();

    /// <summary>실제로는 <see cref="FlowEngine.DeployAsync"/>가 이 호출을 건너뜁니다(위 remarks) — 인터페이스 계약을 위해서만 존재.</summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>입력을 받아도 그냥 버립니다(출력 없음) — Node-RED "missing" 노드와 동일한 동작.</summary>
    public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
}
