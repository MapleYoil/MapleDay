using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SundayAnnouncementTests
{
    private static DateTimeOffset Korea(int day, int hour = 10, int second = 0) => new(2026, 10, day, hour, 0, second, TimeSpan.FromHours(9));

    [Theory]
    [InlineData(0, 9)]
    [InlineData(1, 8)]
    [InlineData(2, 7)]
    [InlineData(3, 6)]
    public void ConsecutiveHolidaysMoveFridayToLastBusinessDay(int count, int expected)
    {
        var holidays = Enumerable.Range(0, count).Select(offset => new DateOnly(2026, 10, 9).AddDays(-offset)).ToHashSet();
        var window = SundayAnnouncements.Window(new(2026, 10, 11), holidays.Contains);
        Assert.Equal(Korea(expected), window.Due);
        Assert.True(window.IsDue(Korea(expected)));
        Assert.False(window.IsDue(Korea(expected, 9)));
        Assert.False(window.IsDue(Korea(expected).AddDays(1)));
        if (count > 0) Assert.False(window.IsDue(Korea(9)));
    }

    [Fact]
    public void RetryIsFiveSecondsAfterFailureAndNeverSpillsToFridayOrDuplicates()
    {
        var window = SundayAnnouncements.Window(new(2026, 10, 11), KoreanPublicHolidays.ForYear(2026).Contains);
        var retry = Korea(8).AddSeconds(5);
        Assert.False(SundayAnnouncements.ShouldAttempt(window, Korea(8, second: 4), retry, true, false));
        Assert.True(SundayAnnouncements.ShouldAttempt(window, retry, retry, true, false));
        Assert.False(SundayAnnouncements.ShouldAttempt(window, retry, retry, false, false));
        Assert.False(SundayAnnouncements.ShouldAttempt(window, retry, retry, true, true));
        Assert.False(SundayAnnouncements.ShouldAttempt(window, Korea(9), retry, true, false));
        Assert.True(window.IsDue(new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero)));
        Assert.False(window.IsDue(new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void LunarHolidayRunAndYearBoundarySkipEveryHolidayAndWeekend()
    {
        var holidays = KoreanPublicHolidays.ForYear(2026);
        Assert.Contains(new DateOnly(2026, 9, 24), holidays);
        Assert.Contains(new DateOnly(2026, 9, 25), holidays);
        Assert.Equal(new DateOnly(2026, 9, 23), ScheduleCalendar.KoreanDate(SundayAnnouncements.Window(new(2026, 9, 27), holidays.Contains).Due));
        Assert.Contains(new DateOnly(2026, 5, 25), holidays);
        Assert.Contains(new DateOnly(2026, 10, 5), holidays);
        Assert.Contains(new DateOnly(2026, 5, 1), holidays);
        Assert.Contains(new DateOnly(2026, 7, 17), holidays);
        bool Holiday(DateOnly date) => KoreanPublicHolidays.ForYear(date.Year).Contains(date);
        Assert.Equal(new DateOnly(2026, 12, 31), ScheduleCalendar.KoreanDate(SundayAnnouncements.Window(new(2027, 1, 3), Holiday).Due));
        var run = Enumerable.Range(0, 5).Select(offset => new DateOnly(2026, 10, 9).AddDays(-offset)).ToHashSet();
        Assert.Equal(new DateOnly(2026, 10, 2), ScheduleCalendar.KoreanDate(SundayAnnouncements.Window(new(2026, 10, 11), run.Contains).Due));
    }

    [Fact]
    public void PublishedRequiresMatchingSundayAndActualBenefitImages()
    {
        var sunday = new DateOnly(2026, 10, 11);
        SundayNotice Previous() => new(1, sunday.AddDays(-7), sunday.AddDays(-7), "https://maplestory.nexon.com/News/Event/1", ["old"]);
        Assert.Null(SundayAnnouncements.Published([Previous(), new(2, sunday, sunday, "url", [])], sunday));
        var current = new SundayNotice(3, sunday, sunday, "https://maplestory.nexon.com/News/Event/3", ["benefits"]);
        Assert.Equal(current, SundayAnnouncements.Published([Previous(), current], sunday));
    }

    [Fact]
    public void AnnualHolidayTableReadsRangesAndTemporaryDaysWithoutCommemorations()
    {
        var rows = string.Join("", Enumerable.Range(1, 12).Select(month => $"<tr><td>휴일</td><td>{month}월 1일</td></tr>"));
        var html = "2026년 달력자료<h2>국경일과 공휴일</h2><table><tbody>" + rows
            + "<tr><td>추석</td><td>9월24일~ 9월26일</td><td>임시공휴일</td><td>10월 8일</td></tr>"
            + "<tr><td>국경일**</td><td>7월 17일</td></tr></tbody></table><h2>기념일</h2><td>10월 9일</td>";
        var dates = KoreanPublicHolidays.ParseAnnualCalendar(html, 2026);
        Assert.Contains(new DateOnly(2026, 9, 25), dates);
        Assert.Contains(new DateOnly(2026, 10, 8), dates);
        Assert.DoesNotContain(new DateOnly(2026, 7, 17), dates);
        Assert.DoesNotContain(new DateOnly(2026, 10, 9), dates);
        Assert.Throws<InvalidDataException>(() => KoreanPublicHolidays.ParseAnnualCalendar(html, 2027));
        Assert.Throws<InvalidDataException>(() => KoreanPublicHolidays.ParseAnnualCalendar("2026년 달력자료", 2026));
    }
}
