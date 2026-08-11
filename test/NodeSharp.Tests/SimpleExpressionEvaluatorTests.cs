using NodeSharp.Contracts.Models;
using NodeSharp.Util.Evaluation;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SimpleExpressionEvaluator"/>(NR-04, FN-01 완료 전까지 쓰는 임시 사칙연산 평가기)에 대한
/// 단위 테스트입니다. <c>TypedValueSource.cs</c> XML 문서의 예제 수식(<c>"payload * 1.8 + 32"</c>)을
/// 그대로 검증해 실제 문서 예제가 동작함을 확인합니다.
/// </summary>
public class SimpleExpressionEvaluatorTests
{
    [Fact]
    public void TypedValueSource_문서_예제_수식이_정상_계산된다()
    {
        var msg = new Msg { Payload = 20.0 };
        var result = SimpleExpressionEvaluator.Evaluate("payload * 1.8 + 32", msg);
        Assert.Equal(68.0, Assert.IsType<double>(result));
    }

    [Fact]
    public void 괄호와_연산자_우선순위가_올바르게_적용된다()
    {
        var msg = new Msg();
        var result = SimpleExpressionEvaluator.Evaluate("(2 + 3) * 4", msg);
        Assert.Equal(20.0, result);
    }

    [Fact]
    public void 단항_마이너스가_동작한다()
    {
        var msg = new Msg();
        var result = SimpleExpressionEvaluator.Evaluate("-5 + 10", msg);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void 문자열_리터럴끼리는_연결된다()
    {
        var msg = new Msg { Payload = "world" };
        var result = SimpleExpressionEvaluator.Evaluate("'hello ' + payload", msg);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void 중첩된_msg_필드_경로도_참조할_수_있다()
    {
        var msg = new Msg();
        dynamic dyn = msg;
        dyn.threshold = 10.0;
        var result = SimpleExpressionEvaluator.Evaluate("threshold + 5", msg);
        Assert.Equal(15.0, result);
    }

    [Fact]
    public void 지원하지_않는_비교_연산자는_FormatException을_던진다()
    {
        var msg = new Msg { Payload = 10.0 };
        Assert.Throws<FormatException>(() => SimpleExpressionEvaluator.Evaluate("payload > 5", msg));
    }

    [Fact]
    public void 닫는_괄호가_없으면_FormatException을_던진다()
    {
        var msg = new Msg();
        Assert.Throws<FormatException>(() => SimpleExpressionEvaluator.Evaluate("(1 + 2", msg));
    }
}
