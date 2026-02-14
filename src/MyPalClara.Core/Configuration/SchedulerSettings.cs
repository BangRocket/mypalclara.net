namespace MyPalClara.Core.Configuration;

public sealed class SchedulerSettings
{
    public bool Enabled { get; set; } = false;
    public List<ScheduledJobSettings> Jobs { get; set; } = [];
}

public sealed class ScheduledJobSettings
{
    public string Name { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
