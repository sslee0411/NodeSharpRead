using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="CronExpression"/>(RT-08, <c>IScheduler.ScheduleCron</c>이 요구하는 문자열 해석기)에 대한
/// 단위 테스트입니다. 이 버전은 <c>*</c>(모든 값)와 쉼표로 구분한 숫자 목록만 지원합니다(범위·간격
/// 문법은 범위 밖 — CronExpression.cs XML 주석 참고).
/// </summary>
public class CronExpressionTests
{
    [Fact]
    public void 매시_정각_표현식은_0분_0초에만_일치한다()
    {
        var cron = CronExpression.Parse("0 0 * * * *");

        Assert.True(cron.IsMatch(new DateTime(2026, 8, 1, 14, 0, 0)));
        Assert.False(cron.IsMatch(new DateTime(2026, 8, 1, 14, 0, 1)));   // 초가 다름
        Assert.False(cron.IsMatch(new DateTime(2026, 8, 1, 14, 5, 0)));   // 분이 다름
    }

    [Fact]
    public void 와일드카드는_모든_값에_일치한다()
    {
        var cron = CronExpression.Parse("* * * * * *");

        Assert.True(cron.IsMatch(new DateTime(2026, 1, 1, 0, 0, 0)));
        Assert.True(cron.IsMatch(new DateTime(2099, 12, 31, 23, 59, 59)));
    }

    [Fact]
    public void 쉼표로_구분한_여러_값_중_하나만_맞아도_일치한다()
    {
        var cron = CronExpression.Parse("0 0,15,30,45 * * * *");   // 매시 0/15/30/45분 정각

        Assert.True(cron.IsMatch(new DateTime(2026, 8, 1, 9, 15, 0)));
        Assert.True(cron.IsMatch(new DateTime(2026, 8, 1, 9, 45, 0)));
        Assert.False(cron.IsMatch(new DateTime(2026, 8, 1, 9, 20, 0)));
    }

    [Fact]
    public void 필드가_6개가_아니면_FormatException을_던진다()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * * *"));   // 5개뿐
    }

    [Fact]
    public void 필드_값이_허용_범위를_벗어나면_FormatException을_던진다()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 25 * * *"));   // 시(hour)는 0~23까지만
    }

    [Fact]
    public void 요일_필드는_DayOfWeek_숫자_체계를_그대로_쓴다()
    {
        var mondayOnly = CronExpression.Parse("0 0 9 * * 1");   // 매주 월요일 9시 정각
        var monday = new DateTime(2026, 8, 3, 9, 0, 0);   // 2026-08-03은 월요일
        var tuesday = new DateTime(2026, 8, 4, 9, 0, 0);

        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.True(mondayOnly.IsMatch(monday));
        Assert.False(mondayOnly.IsMatch(tuesday));
    }
}
