using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : Function 실행기 계약
/// 역활 및 기능 : Function 노드가 사용자 코드/표현식을 실제로 계산하는 로직을 감싸는 전략 패턴 인터페이스
///
/// Function 노드의 실제 계산 로직(NCalc 표현식 평가 또는 Roslyn C# 코드 실행)을 감싸는 전략
/// 패턴 인터페이스입니다. "표현식이냐 코드냐"를 <c>FunctionNode</c> 안에서 if/else로 직접
/// 분기하면 코드가 지저분해지고 테스트하기도 어려워, 실행 로직 자체를 이 인터페이스로 뽑아냈습니다
/// — 나중에 세 번째 모드가 추가돼도 <c>FunctionNode</c> 코드는 건드릴 필요 없이 구현체만 하나
/// 더 만들면 됩니다.
/// 설계 근거: 02번 문서 5번 탭 카드5.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>구현체 위치</b>: <c>NCalcFunctionExecutor</c>(FN-01)는 <c>nodes\NodeSharp.Nodes.Function</c>
/// 프로젝트에 있습니다 — <c>TypedValueEvaluator</c>(NR-04)처럼 여러 노드가 공유하는 평가기가
/// 아니라 Function 노드 전용 NCalc 패키지 의존성이라, Util에 두지 않고 노드 플러그인 프로젝트
/// 안에 그대로 둡니다. <c>RoslynFunctionExecutor</c>(FN-02)도 같은 이유로 같은 프로젝트에 위치할
/// 예정입니다.</item>
/// <item><b>예외 처리 책임</b>: <see cref="ExecuteAsync"/>는 <c>ctx</c>를 받지 않으므로 노드 상태
/// 표시(<c>INodeContext.SetStatus</c>)를 스스로 할 수 없습니다 — 표현식/코드 오류로 던진 예외를
/// 잡아 노드 에러로 표면화하는 책임은 호출자인 <c>FunctionNode.OnInputAsync</c>가 집니다(FN-01
/// 항목 참고, <c>FlowEngine.RouteAsync</c>가 대상 노드 예외를 격리하지 않는 RT-04a 경계 때문에
/// 반드시 필요).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public sealed class EchoExecutor : IFunctionExecutor
/// {
///     public void Prepare(string userCode) { /* 파싱/컴파일 준비 */ }
///
///     public Task&lt;Msg?&gt; ExecuteAsync(Msg msg, CancellationToken ct)
///     {
///         msg.Payload = $"echo: {msg.Payload}";
///         return Task.FromResult&lt;Msg?&gt;(msg);   // null을 반환하면 다음 노드로 전달되지 않음(필터링)
///     }
/// }
/// </code>
/// </example>
public interface IFunctionExecutor
{
    /// <summary>사용자 코드/표현식을 준비(파싱 또는 컴파일)합니다. <c>FunctionNode.OnStartAsync</c>에서 1회만 호출됩니다.</summary>
    void Prepare(string userCode);

    /// <summary>
    /// <paramref name="msg"/> 1개를 입력받아 계산 후 결과 <see cref="Msg"/>를 반환합니다. <c>null</c>을
    /// 반환하면 이 메시지는 다음 노드로 전달되지 않습니다(필터링 용도, Node-RED의 <c>return null;</c>과 동일).
    /// 표현식/코드 문법 오류 등은 예외로 던지며, 잡는 책임은 호출자(<c>FunctionNode</c>)에게 있습니다.
    /// </summary>
    Task<Msg?> ExecuteAsync(Msg msg, CancellationToken ct);
}
