namespace Clara.Gateway.Services;

public static class CronParser
{
    /// <summary>
    /// Returns true if the given time matches the 5-field cron expression.
    /// Fields: minute hour day-of-month month day-of-week
    /// Supports: *, */N, N-M, N,M, specific values
    /// </summary>
    public static bool Matches(string expression, DateTime time)
    {
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new FormatException($"Invalid cron expression: expected 5 fields, got {fields.Length}");

        return FieldMatches(fields[0], time.Minute, 0, 59)
            && FieldMatches(fields[1], time.Hour, 0, 23)
            && FieldMatches(fields[2], time.Day, 1, 31)
            && FieldMatches(fields[3], time.Month, 1, 12)
            && FieldMatches(fields[4], (int)time.DayOfWeek, 0, 6); // Sunday = 0
    }

    /// <summary>
    /// Returns the next occurrence after 'from' that matches the cron expression.
    /// Walks forward minute by minute, capped at 366 days to prevent infinite loops.
    /// </summary>
    public static DateTime? GetNextOccurrence(string expression, DateTime from)
    {
        // Start from the next minute (truncated to minute)
        var candidate = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0, from.Kind)
            .AddMinutes(1);
        var limit = from.AddDays(366);

        while (candidate <= limit)
        {
            if (Matches(expression, candidate))
                return candidate;
            candidate = candidate.AddMinutes(1);
        }

        return null; // No match within 366 days
    }

    private static bool FieldMatches(string field, int value, int min, int max)
    {
        // Handle comma-separated values (e.g., "1,15,30")
        if (field.Contains(','))
        {
            return field.Split(',').Any(part => FieldMatches(part.Trim(), value, min, max));
        }

        // Wildcard
        if (field == "*")
            return true;

        // Step (*/N or N-M/S)
        if (field.Contains('/'))
        {
            var parts = field.Split('/');
            var step = int.Parse(parts[1]);

            if (parts[0] == "*")
                return (value - min) % step == 0;

            // Range with step: "N-M/S"
            if (parts[0].Contains('-'))
            {
                var rangeParts = parts[0].Split('-');
                var rangeStart = int.Parse(rangeParts[0]);
                var rangeEnd = int.Parse(rangeParts[1]);
                return value >= rangeStart && value <= rangeEnd && (value - rangeStart) % step == 0;
            }

            // Single value with step makes no sense, but treat as just start/step
            var start = int.Parse(parts[0]);
            return value >= start && (value - start) % step == 0;
        }

        // Range (N-M)
        if (field.Contains('-'))
        {
            var parts = field.Split('-');
            var rangeStart = int.Parse(parts[0]);
            var rangeEnd = int.Parse(parts[1]);
            return value >= rangeStart && value <= rangeEnd;
        }

        // Specific value
        return int.Parse(field) == value;
    }
}
