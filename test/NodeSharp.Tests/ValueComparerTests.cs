using NodeSharp.Util.Evaluation;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ValueComparer"/>(NR-04, NodeSharp.Util.Evaluation)에 대한 단위 테스트입니다 — 숫자 우선
/// 비교/동등 판정이 Switch 노드의 lt/lte/gt/gte/btwn/eq/neq 연산자가 기대하는 대로 동작하는지 확인합니다.
/// </summary>
public class ValueComparerTests
{
    [Theory]
    [InlineData(5, 10, -1)]
    [InlineData(10, 5, 1)]
    [InlineData(5, 5, 0)]
    public void Compare는_숫자끼리는_숫자로_비교한다(double a, double b, int expectedSign)
    {
        var result = ValueComparer.Compare(a, b);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Fact]
    public void Compare는_숫자로_변환되는_문자열도_숫자로_비교한다()
    {
        // 사전순이면 "10"이 "9"보다 앞서지만("1" < "9"), 숫자 비교라면 9 < 10이라 "9"가 더 작다.
        Assert.True(ValueComparer.Compare("9", "10") < 0);
    }

    [Fact]
    public void Compare는_숫자로_변환_안_되는_값은_문자열로_비교한다()
    {
        Assert.True(ValueComparer.Compare("apple", "banana") < 0);
    }

    [Fact]
    public void LooseEquals는_int와_같은_값의_문자열을_같다고_판정한다()
    {
        Assert.True(ValueComparer.LooseEquals(42, "42"));
    }

    [Fact]
    public void LooseEquals는_숫자로_변환_안_되는_다른_문자열은_다르다고_판정한다()
    {
        Assert.False(ValueComparer.LooseEquals("abc", "def"));
    }

    [Fact]
    public void LooseEquals는_둘_다_null이면_같다고_판정한다()
    {
        Assert.True(ValueComparer.LooseEquals(null, null));
    }

    [Fact]
    public void LooseEquals는_하나만_null이면_다르다고_판정한다()
    {
        Assert.False(ValueComparer.LooseEquals(null, "42"));
    }

    [Fact]
    public void TryToDouble은_bool값은_변환하지_못한다()
    {
        Assert.False(ValueComparer.TryToDouble(true, out _));
    }
}
