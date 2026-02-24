namespace MyPalClara.Gateway.Tests.Scheduling;

using MyPalClara.Gateway.Scheduling;

public class CronParserTests
{
    [Fact]
    public void Parse_EveryMinute()
    {
        var cron = CronParser.Parse("* * * * *");
        var from = new DateTime(2026, 2, 24, 10, 30, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 31, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_SpecificMinute()
    {
        var cron = CronParser.Parse("15 * * * *");
        var from = new DateTime(2026, 2, 24, 10, 30, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 11, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_StepExpression()
    {
        var cron = CronParser.Parse("*/15 * * * *");
        var from = new DateTime(2026, 2, 24, 10, 7, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_DailyAt9AM()
    {
        var cron = CronParser.Parse("0 9 * * *");
        var from = new DateTime(2026, 2, 24, 10, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 25, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_RangeExpression()
    {
        var cron = CronParser.Parse("0 9-17 * * *");
        var from = new DateTime(2026, 2, 24, 8, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_ListExpression()
    {
        var cron = CronParser.Parse("0 9,12,18 * * *");
        var from = new DateTime(2026, 2, 24, 10, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 12, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_InvalidFieldCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => CronParser.Parse("* * *"));
    }

    [Fact]
    public void Parse_DayOfWeek_Monday()
    {
        // Monday = 1 in DayOfWeek
        var cron = CronParser.Parse("0 9 * * 1");
        // 2026-02-24 is a Tuesday
        var from = new DateTime(2026, 2, 24, 10, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        // Next Monday is March 2
        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_RangeWithStep()
    {
        // Minutes 0-30 with step of 10 => 0, 10, 20, 30
        var cron = CronParser.Parse("0-30/10 * * * *");
        var from = new DateTime(2026, 2, 24, 10, 5, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 10, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_SpecificDayOfMonth()
    {
        // First of every month at midnight
        var cron = CronParser.Parse("0 0 1 * *");
        var from = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_SpecificMonth()
    {
        // January 1st at midnight
        var cron = CronParser.Parse("0 0 1 1 *");
        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_FromMiddleOfMinute_TruncatesToMinuteBoundary()
    {
        var cron = CronParser.Parse("* * * * *");
        // From 10:30:45 -- should still go to 10:31:00
        var from = new DateTime(2026, 2, 24, 10, 30, 45, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 31, 0, DateTimeKind.Utc), next);
    }
}
