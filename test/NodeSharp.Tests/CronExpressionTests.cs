using NodeSharp.Util.Messaging;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="CronExpression"/>(RT-08, <c>IScheduler.ScheduleCron</c>이 요구하는 문자열 해석기)에 대한
/// 단위 테스트입니다. 이 버전은 <c>*</c>(모든 값)와 쉼표로 구분한 숫자 목록만 지원합니다(범위·간격
/// 문법은 범위 밖 — CronExpression.cs XML 주석 참고). (NR-03d) 6필드("초 분 시 일 월 요일")뿐 아니라
/// 5필드(표준 cron "분 시 일 월 요일", 초는 0으로 간주)도 지원하도록 확장 — Inject의 cron 트리거가
/// 이 확장을 실제로 사용합니다(InjectNode.cs NR-03d 항목 참고).
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
    public void 필드가_5개도_6개도_아니면_FormatException을_던진다()
    {
        // (NR-03d) 5필드도 이제 허용되므로, 이전엔 "5개뿐이라 예외"였던 "0 0 * * *"는 더 이상 예외가
        // 아님 — 실제로 예외여야 하는 4개/7개 필드로 갱신(v2.74에서 겪은 것과 같은 실수를 여기서는
        // 구현과 동시에 미리 반영).
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * *"));       // 4개뿐
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 0 * * * *")); // 7개(초과)
    }

    [Fact]
    public void 완료_기준_직접_검증__5필드_입력은_초를_0으로_간주해_6필드와_동등하게_동작한다()
    {
        // (NR-03d) 03번 Step맵 NR-03d 완료 기준의 예시 "* * * * *"(5필드, 표준 cron)가 실제로 파싱되고,
        // 6필드로 직접 쓴 "0 * * * * *"와 동일하게 동작하는지 확인.
        var fiveField = CronExpression.Parse("* * * * *");
        var sixFieldEquivalent = CronExpression.Parse("0 * * * * *");

        var atZeroSeconds = new DateTime(2026, 8, 1, 14, 5, 0);
        var atNonZeroSeconds = new DateTime(2026, 8, 1, 14, 5, 30);

        Assert.True(fiveField.IsMatch(atZeroSeconds));
        Assert.False(fiveField.IsMatch(atNonZeroSeconds));   // 초는 0으로 간주 — 30초에는 불일치
        Assert.Equal(sixFieldEquivalent.IsMatch(atZeroSeconds), fiveField.IsMatch(atZeroSeconds));
        Assert.Equal(sixFieldEquivalent.IsMatch(atNonZeroSeconds), fiveField.IsMatch(atNonZeroSeconds));
    }

    [Fact]
    public void 완료_기준_직접_검증__5필드_요일_지정도_정상_동작한다()
    {
        var mondayOnlyFiveField = CronExpression.Parse("0 9 * * 1");   // 매주 월요일 9시 정각(5필드)
        var monday = new DateTime(2026, 8, 3, 9, 0, 0);   // 2026-08-03은 월요일
        var tuesday = new DateTime(2026, 8, 4, 9, 0, 0);

        Assert.True(mondayOnlyFiveField.IsMatch(monday));
        Assert.False(mondayOnlyFiveField.IsMatch(tuesday));
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
