namespace MyPalClara.Modules.Proactive.Context;

public static class TemporalContext
{
    public static string Gather()
    {
        var now = DateTime.UtcNow;
        var dayOfWeek = now.DayOfWeek;
        var hour = now.Hour;
        var timeOfDay = hour switch
        {
            < 6 => "early morning",
            < 12 => "morning",
            < 17 => "afternoon",
            < 21 => "evening",
            _ => "night"
        };
        return $"Day: {dayOfWeek}, Time: {timeOfDay} ({hour}:00 UTC), Date: {now:yyyy-MM-dd}";
    }
}
