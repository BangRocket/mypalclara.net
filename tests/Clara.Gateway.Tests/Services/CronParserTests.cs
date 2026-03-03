using Clara.Gateway.Services;

namespace Clara.Gateway.Tests.Services;

public class CronParserTests
{
    [Fact]
    public void Wildcard_matches_any_time()
    {
        var time = new DateTime(2025, 6, 15, 14, 30, 0);
        Assert.True(CronParser.Matches("* * * * *", time));
    }

    [Fact]
    public void Specific_minute_and_hour()
    {
        // 0 9 * * * = 9:00 AM every day
        var match = new DateTime(2025, 6, 15, 9, 0, 0);
        var noMatch = new DateTime(2025, 6, 15, 10, 0, 0);

        Assert.True(CronParser.Matches("0 9 * * *", match));
        Assert.False(CronParser.Matches("0 9 * * *", noMatch));
    }

    [Fact]
    public void Step_every_5_minutes()
    {
        // */5 * * * * = every 5 minutes
        Assert.True(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 0, 0)));
        Assert.True(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 5, 0)));
        Assert.True(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 10, 0)));
        Assert.True(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 55, 0)));
        Assert.False(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 3, 0)));
        Assert.False(CronParser.Matches("*/5 * * * *", new DateTime(2025, 1, 1, 0, 7, 0)));
    }

    [Fact]
    public void Comma_separated_values()
    {
        // 1,15,30 * * * * = at minutes 1, 15, 30
        Assert.True(CronParser.Matches("1,15,30 * * * *", new DateTime(2025, 1, 1, 0, 1, 0)));
        Assert.True(CronParser.Matches("1,15,30 * * * *", new DateTime(2025, 1, 1, 0, 15, 0)));
        Assert.True(CronParser.Matches("1,15,30 * * * *", new DateTime(2025, 1, 1, 0, 30, 0)));
        Assert.False(CronParser.Matches("1,15,30 * * * *", new DateTime(2025, 1, 1, 0, 2, 0)));
        Assert.False(CronParser.Matches("1,15,30 * * * *", new DateTime(2025, 1, 1, 0, 45, 0)));
    }

    [Fact]
    public void Range_weekday_business_hours()
    {
        // 0 9-17 * * 1-5 = on the hour, 9AM-5PM, Monday-Friday
        // Monday = 1 in DayOfWeek
        var mondayNoon = new DateTime(2025, 6, 16, 12, 0, 0); // Monday
        Assert.True(CronParser.Matches("0 9-17 * * 1-5", mondayNoon));

        var mondayMorning9 = new DateTime(2025, 6, 16, 9, 0, 0);
        Assert.True(CronParser.Matches("0 9-17 * * 1-5", mondayMorning9));

        var mondayEvening6 = new DateTime(2025, 6, 16, 18, 0, 0);
        Assert.False(CronParser.Matches("0 9-17 * * 1-5", mondayEvening6));

        // Sunday = 0
        var sundayNoon = new DateTime(2025, 6, 15, 12, 0, 0); // Sunday
        Assert.False(CronParser.Matches("0 9-17 * * 1-5", sundayNoon));

        // Saturday = 6
        var saturdayNoon = new DateTime(2025, 6, 14, 12, 0, 0); // Saturday
        Assert.False(CronParser.Matches("0 9-17 * * 1-5", saturdayNoon));
    }

    [Fact]
    public void Specific_day_of_month()
    {
        // 0 0 1 * * = midnight on the 1st of every month
        Assert.True(CronParser.Matches("0 0 1 * *", new DateTime(2025, 3, 1, 0, 0, 0)));
        Assert.False(CronParser.Matches("0 0 1 * *", new DateTime(2025, 3, 2, 0, 0, 0)));
    }

    [Fact]
    public void Specific_month()
    {
        // 0 0 1 1 * = midnight Jan 1
        Assert.True(CronParser.Matches("0 0 1 1 *", new DateTime(2025, 1, 1, 0, 0, 0)));
        Assert.False(CronParser.Matches("0 0 1 1 *", new DateTime(2025, 2, 1, 0, 0, 0)));
    }

    [Fact]
    public void Invalid_field_count_throws()
    {
        Assert.Throws<FormatException>(() =>
            CronParser.Matches("* * *", new DateTime(2025, 1, 1)));
    }

    [Fact]
    public void GetNextOccurrence_finds_next_minute()
    {
        var from = new DateTime(2025, 1, 1, 12, 30, 45);
        var next = CronParser.GetNextOccurrence("* * * * *", from);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2025, 1, 1, 12, 31, 0), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_finds_specific_time()
    {
        // Next 9:00 AM after 10:00 AM should be tomorrow
        var from = new DateTime(2025, 6, 15, 10, 0, 0);
        var next = CronParser.GetNextOccurrence("0 9 * * *", from);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2025, 6, 16, 9, 0, 0), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_returns_null_for_impossible_expression()
    {
        // February 31st will never exist
        var from = new DateTime(2025, 1, 1, 0, 0, 0);
        var next = CronParser.GetNextOccurrence("0 0 31 2 *", from);

        Assert.Null(next);
    }
}
