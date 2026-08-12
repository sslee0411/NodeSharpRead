using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Function;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="NCalcFunctionExecutor"/>(FN-01, 03번 개발 Step맵 Phase 7 — Function 노드 NCalc 표현식
/// 실행기)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 FN-01): "수식 문법 오류(괄호 불일치 등)를
/// 입력해도 Runner가 크래시하지 않고 노드 에러로만 표면화되는지, 컴파일 없이 즉시 반영되는지 확인" —
/// 이 클래스는 "문법 오류 시 예외가 던져지는지"(호출자가 잡는 부분은 FunctionNodeTests가 검증)와
/// "Prepare가 컴파일 없이 즉시 표현식을 반영하는지"를 다룹니다.
/// </summary>
public class NCalcFunctionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync는_사칙연산_표현식을_계산해_Payload에_저장한다()
    {
        var executor = new NCalcFunctionExecutor();
        executor.Prepare("2 + 3 * 5");

        var msg = new Msg();
        var result = await executor.ExecuteAsync(msg, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(17, Convert.ToInt32(result!.Payload));
    }

    [Fact]
    public async Task ExecuteAsync는_msg의_동적_필드를_표현식_변수로_주입한다()
    {
        var executor = new NCalcFunctionExecutor();
        executor.Prepare("(pressure1 - pressure2) * 0.0689");

        var msg = new Msg();
        dynamic dyn = msg;
        dyn.pressure1 = 100.0;
        dyn.pressure2 = 20.0;

        var result = await executor.ExecuteAsync(msg, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(80.0 * 0.0689, Convert.ToDouble(result!.Payload), precision: 6);
    }

    [Fact]
    public async Task ExecuteAsync는_payload를_명시적으로도_변수로_보장한다()
    {
        var executor = new NCalcFunctionExecutor();
        executor.Prepare("payload * 2");

        var msg = new Msg { Payload = 21 };
        var result = await executor.ExecuteAsync(msg, CancellationToken.None);

        Assert.Equal(42, Convert.ToInt32(result!.Payload));
    }

    [Fact]
    public async Task ExecuteAsync는_문법_오류_표현식이면_예외를_던진다()
    {
        var executor = new NCalcFunctionExecutor();
        executor.Prepare("(1 + 2"); // 괄호 불일치

        var msg = new Msg();

        await Assert.ThrowsAnyAsync<Exception>(() => executor.ExecuteAsync(msg, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare는_컴파일_없이_즉시_새_표현식을_반영한다()
    {
        var executor = new NCalcFunctionExecutor();

        executor.Prepare("1 + 1");
        var first = await executor.ExecuteAsync(new Msg(), CancellationToken.None);
        Assert.Equal(2, Convert.ToInt32(first!.Payload));

        executor.Prepare("10 * 10"); // 재컴파일/캐시 갱신 절차 없이 다음 호출에 즉시 반영돼야 함
        var second = await executor.ExecuteAsync(new Msg(), CancellationToken.None);
        Assert.Equal(100, Convert.ToInt32(second!.Payload));
    }
}
