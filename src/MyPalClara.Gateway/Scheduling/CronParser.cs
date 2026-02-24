namespace MyPalClara.Gateway.Scheduling;

public class CronExpression
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _daysOfMonth;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _daysOfWeek;

    public CronExpression(HashSet<int> minutes, HashSet<int> hours, HashSet<int> daysOfMonth, HashSet<int> months, HashSet<int> daysOfWeek)
    {
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    public DateTime GetNextOccurrence(DateTime from)
    {
        // Start from next minute (truncated to minute boundary)
        var candidate = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0, from.Kind)
            .AddMinutes(1);

        // Search up to 4 years
        var limit = from.AddYears(4);

        while (candidate < limit)
        {
            if (!_months.Contains(candidate.Month))
            {
                // Skip to first day of next month
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, candidate.Kind).AddMonths(1);
                continue;
            }

            if (!_daysOfMonth.Contains(candidate.Day) || !_daysOfWeek.Contains((int)candidate.DayOfWeek))
            {
                // Skip to next day
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, 0, 0, 0, candidate.Kind).AddDays(1);
                continue;
            }

            if (!_hours.Contains(candidate.Hour))
            {
                // Skip to next hour
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0, candidate.Kind).AddHours(1);
                continue;
            }

            if (!_minutes.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return candidate;
        }

        throw new InvalidOperationException("No next occurrence found within 4 years");
    }
}

public static class CronParser
{
    public static CronExpression Parse(string expression)
    {
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new ArgumentException($"Cron expression must have 5 fields, got {parts.Length}");

        return new CronExpression(
            ParseField(parts[0], 0, 59),
            ParseField(parts[1], 0, 23),
            ParseField(parts[2], 1, 31),
            ParseField(parts[3], 1, 12),
            ParseField(parts[4], 0, 6));
    }

    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var values = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                // Step: */N or M-N/S
                var slashParts = part.Split('/');
                var step = int.Parse(slashParts[1]);
                int rangeStart, rangeEnd;

                if (slashParts[0] == "*")
                {
                    rangeStart = min;
                    rangeEnd = max;
                }
                else if (slashParts[0].Contains('-'))
                {
                    var rangeParts = slashParts[0].Split('-');
                    rangeStart = int.Parse(rangeParts[0]);
                    rangeEnd = int.Parse(rangeParts[1]);
                }
                else
                {
                    rangeStart = int.Parse(slashParts[0]);
                    rangeEnd = max;
                }

                for (var i = rangeStart; i <= rangeEnd; i += step)
                    values.Add(i);
            }
            else if (part.Contains('-'))
            {
                // Range: M-N
                var rangeParts = part.Split('-');
                var start = int.Parse(rangeParts[0]);
                var end = int.Parse(rangeParts[1]);
                for (var i = start; i <= end; i++)
                    values.Add(i);
            }
            else if (part == "*")
            {
                for (var i = min; i <= max; i++)
                    values.Add(i);
            }
            else
            {
                values.Add(int.Parse(part));
            }
        }

        return values;
    }
}
