using NCalc;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : NCalc 표현식 실행기
/// 역활 및 기능 : NCalc 라이브러리로 사용자가 입력한 한 줄 수식을 msg 필드 값과 함께 계산하는 IFunctionExecutor 구현체
///
/// NCalc 라이브러리(NuGet <c>NCalc</c> 6.4.0)로 사용자가 입력한 한 줄 수식을 계산하는
/// <see cref="IFunctionExecutor"/> 구현체입니다. 코드를 몰라도 되는 경량 모드로,
/// <see cref="Msg"/>가 <see cref="System.Dynamic.ExpandoObject"/> 기반 동적 객체라 표현식
/// 안에서도 <c>payload</c>·<c>topic</c>·임의 필드를 변수처럼 그대로 쓸 수 있습니다.
/// 설계 근거: 02번 문서 5번 탭 카드6, 03번 개발 Step맵 FN-01.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>매 실행마다 재파싱</b>: <see cref="Prepare"/>는 표현식 문자열만 저장하고,
/// <see cref="ExecuteAsync"/>가 호출될 때마다 새 <c>NCalc.Expression</c>을 만듭니다. NCalc 표현식은
/// 가볍고 캐시 이득이 크지 않아(Roslyn 컴파일과 달리 파싱 자체가 빠름) 캐시를 두지 않았습니다 —
/// 카드6 원본 스니펫과 동일한 설계.</item>
/// <item><b>예외는 그대로 던진다</b>: 문법 오류가 있는 표현식(예: 괄호 불일치)을 <see cref="ExecuteAsync"/>가
/// 계산하면 NCalc가 예외를 던지고, 이 클래스는 잡지 않고 그대로 전파합니다 — 잡는 책임은 호출자인
/// <c>FunctionNode.OnInputAsync</c>에 있습니다(<see cref="IFunctionExecutor"/> XML 문서·
/// <c>FunctionNode</c> 클래스 remarks의 FN-01 항목 참고).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 캔버스 "표현식" 입력란에 사용자가 직접 입력하는 예시들:
/// //   복합 계산   : "(pressure1 - pressure2) * 0.0689"   ← msg.pressure1, msg.pressure2 필드를 그대로 변수처럼 사용
/// //   조건 필터   : "if(val > 0, val, 0)"                ← 0 이하 값은 0으로 치환
/// //   단위 변환   : "(fahrenheit - 32) * 5 / 9"
/// // 계산 결과는 자동으로 msg.payload에 저장됩니다(별도 return 문 불필요).
/// var executor = new NCalcFunctionExecutor();
/// executor.Prepare("(fahrenheit - 32) * 5 / 9");
/// var msg = new Msg();
/// dynamic dyn = msg;
/// dyn.fahrenheit = 98.6;
/// var result = await executor.ExecuteAsync(msg, CancellationToken.None);
/// // result.Payload == 37.0
/// </code>
/// </example>
public sealed class NCalcFunctionExecutor : IFunctionExecutor
{
    /// <summary><see cref="Prepare"/>가 저장해두는 사용자 입력 표현식 문자열입니다.</summary>
    private string _expressionText = string.Empty;

    /// <summary>표현식 문자열을 저장만 합니다 — 실제 파싱은 매번 <see cref="ExecuteAsync"/>가 새 <c>Expression</c>을 만들 때 일어납니다(위 클래스 remarks 참고).</summary>
    public void Prepare(string userCode) => _expressionText = userCode;

    /// <summary>
    /// <paramref name="msg"/>의 모든 필드(<see cref="Msg.Keys"/>)를 표현식 변수로 주입한 뒤 계산하고,
    /// 결과를 <see cref="Msg.Payload"/>에 저장한 같은 <paramref name="msg"/> 인스턴스를 돌려줍니다.
    /// 문법 오류 등으로 NCalc가 던지는 예외는 잡지 않고 그대로 전파합니다(위 클래스 remarks 참고).
    /// </summary>
    public Task<Msg?> ExecuteAsync(Msg msg, CancellationToken ct)
    {
        var expr = new Expression(_expressionText);

        // msg의 모든 동적 필드(payload, topic, 사용자 정의 필드 전부)를 표현식 변수로 주입
        foreach (var key in msg.Keys)
        {
            expr.Parameters[key] = msg.Get<object>(key);
        }

        expr.Parameters["payload"] = msg.Payload; // 자주 쓰는 필드는 명시적으로도 보장

        var result = expr.Evaluate(); // 문법 오류 시 여기서 예외 — 호출자(FunctionNode)가 잡음
        msg.Payload = result;
        return Task.FromResult<Msg?>(msg);
    }
}
