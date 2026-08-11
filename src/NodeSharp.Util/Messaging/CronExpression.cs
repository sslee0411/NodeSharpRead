namespace NodeSharp.Util.Messaging;

/// <summary>
/// Class명 : Cron 표현식
/// 역활 및 기능 : cron 표현식을 해석해 특정 시각이 조건에 맞는지 확인하는 유틸리티
///
/// "초 분 시 일 월 요일" 6칸짜리 cron 표현식(예: <c>"0 0 * * * *"</c> = 매시 정각)을 해석해서, 특정 시각이
/// 그 표현식에 맞는지(<see cref="IsMatch"/>) 확인하는 아주 작은 유틸리티입니다. lssLib에는 대응 항목이
/// 없는 신규 도입 타입입니다 — <c>IScheduler.ScheduleCron</c>(Contracts, CT-04b)이 cron 문자열을 받도록
/// 이미 정해져 있는데, 포팅 대상인 <c>lssLib.Messaging.AsyncScheduler</c>에는 cron 파서가 없어서(반복
/// 간격/1회성/매일 정해진 시각만 지원) 이 계약을 만족시키려면 별도로 필요했습니다(자세한 경위는
/// <see cref="AsyncSchedulerAdapter"/> XML 주석 참고 — NR-03b에서 NodeSharp.Runtime으로부터 이 프로젝트로
/// 이동).
/// 설계 근거: 02번 문서 6번 탭 카드5(<c>IScheduler.ScheduleCron</c>).
/// </summary>
/// <remarks>
/// 이 버전은 각 칸에 <c>*</c>(모든 값 허용) 또는 쉼표로 구분한 숫자 목록(예: <c>"0,15,30,45"</c>)만
/// 지원합니다 — cron 표준의 범위(<c>1-5</c>)나 간격(<c>*/15</c>) 문법은 아직 지원하지 않습니다. 지금
/// 완료 기준(RT-08)이 요구하는 "매시 정각" 같은 단순한 패턴에는 충분하고, 더 복잡한 문법이 실제로
/// 필요해지는 시점(사용 노드가 생기는 Step)에 확장할 예정입니다 — <c>BufFieldType</c>을 처음에 최소
/// 목록으로 시작했다가 나중에 넓힌 것과 같은 방식(CT-03b/v1.42 선례).
/// <para>
/// (NR-03d) <b>5필드 입력도 허용</b> — 착수 당시 6필드("초 분 시 일 월 요일")만 받던 것을, Inject의
/// cron 트리거가 실제 사용하는 시점에 03번 Step맵 NR-03d의 완료 기준 예시("* * * * *", 5필드)와
/// Node-RED가 실제로 쓰는 cronosjs 라이브러리(5~7필드 허용, 5필드면 초는 0이 기본값)를 근거로 확장했습니다
/// — <see cref="Parse"/>가 5필드를 받으면 맨 앞에 초 필드 <c>"0"</c>을 붙여 6필드로 정규화합니다(즉
/// "매분 0초"에 일치 — 매초 일치가 아님에 유의). 6필드 입력은 기존과 동일하게 그대로 동작합니다(하위
/// 호환, 기존 RT-08 호출부·테스트 변경 없음).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var everyHour = CronExpression.Parse("0 0 * * * *");   // 매시 정각(0분 0초) — 6필드
/// bool matches = everyHour.IsMatch(new DateTime(2026, 8, 1, 14, 0, 0));   // true
/// bool notMatches = everyHour.IsMatch(new DateTime(2026, 8, 1, 14, 5, 0)); // false(5분이라 불일치)
///
/// var specificMinutes = CronExpression.Parse("0 0,15,30,45 * * * *");   // 매시 0/15/30/45분 정각
///
/// // (NR-03d) 5필드 — 표준 cron("분 시 일 월 요일") 형식, 초는 0으로 간주
/// var everyMinute = CronExpression.Parse("* * * * *");   // 매분 0초
/// bool alsoMatches = everyMinute.IsMatch(new DateTime(2026, 8, 1, 14, 5, 0));   // true(5분 0초)
/// bool secondMismatch = everyMinute.IsMatch(new DateTime(2026, 8, 1, 14, 5, 30)); // false(30초라 불일치)
/// </code>
/// </example>
public sealed class CronExpression
{
    private readonly HashSet<int>? _seconds;
    private readonly HashSet<int>? _minutes;
    private readonly HashSet<int>? _hours;
    private readonly HashSet<int>? _days;
    private readonly HashSet<int>? _months;
    private readonly HashSet<int>? _daysOfWeek;

    private CronExpression(
        HashSet<int>? seconds, HashSet<int>? minutes, HashSet<int>? hours,
        HashSet<int>? days, HashSet<int>? months, HashSet<int>? daysOfWeek)
    {
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    /// <summary>
    /// <c>"초 분 시 일 월 요일"</c> 형식(공백으로 구분된 6개 필드) 또는 (NR-03d) 표준 cron과 같은
    /// <c>"분 시 일 월 요일"</c> 5개 필드(초는 0으로 간주)의 cron 문자열을 해석합니다. 요일은
    /// 0(일요일)~6(토요일)입니다(<see cref="DayOfWeek"/>와 동일한 숫자 체계).
    /// </summary>
    /// <exception cref="FormatException">필드 개수가 5개·6개가 아니거나, 필드 값이 허용 범위를 벗어날 때.</exception>
    public static CronExpression Parse(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 5)
        {
            // (NR-03d) 5필드(표준 cron "분 시 일 월 요일") — 초 필드를 "0"으로 간주해 6필드로 정규화.
            fields = new[] { "0" }.Concat(fields).ToArray();
        }
        else if (fields.Length != 6)
        {
            throw new FormatException($"cron 표현식은 5개('분 시 일 월 요일', 초는 0으로 간주) 또는 6개" +
                $"('초 분 시 일 월 요일') 필드여야 합니다: '{expression}'");
        }

        return new CronExpression(
            ParseField(fields[0], "초", 0, 59),
            ParseField(fields[1], "분", 0, 59),
            ParseField(fields[2], "시", 0, 23),
            ParseField(fields[3], "일", 1, 31),
            ParseField(fields[4], "월", 1, 12),
            ParseField(fields[5], "요일", 0, 6));
    }

    private static HashSet<int>? ParseField(string field, string fieldName, int min, int max)
    {
        if (field == "*")
        {
            return null;   // null = "모든 값 허용"이라는 의미로 사용(HashSet을 만들지 않아 매 비교마다 메모리 낭비 없음)
        }

        var values = new HashSet<int>();
        foreach (var token in field.Split(','))
        {
            if (!int.TryParse(token, out var value) || value < min || value > max)
            {
                throw new FormatException($"cron {fieldName} 필드 값 '{token}'이(가) 허용 범위 [{min},{max}]를 벗어났습니다: '{field}'");
            }

            values.Add(value);
        }

        return values;
    }

    /// <summary><paramref name="time"/>이 이 cron 표현식이 가리키는 시각과 일치하는지 확인합니다(초 단위까지 비교).</summary>
    public bool IsMatch(DateTime time) =>
        Matches(_seconds, time.Second) &&
        Matches(_minutes, time.Minute) &&
        Matches(_hours, time.Hour) &&
        Matches(_days, time.Day) &&
        Matches(_months, time.Month) &&
        Matches(_daysOfWeek, (int)time.DayOfWeek);

    private static bool Matches(HashSet<int>? allowedValues, int actualValue) =>
        allowedValues is null || allowedValues.Contains(actualValue);
}
