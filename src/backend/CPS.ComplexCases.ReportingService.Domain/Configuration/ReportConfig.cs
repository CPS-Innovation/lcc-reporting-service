namespace CPS.ComplexCases.ReportingService.Domain.Configuration;

public class ReportConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The report's TimerTrigger resolves this same
    /// configuration value, so the schedule must be set here rather than in a separate app setting.
    /// </summary>
    public string CronSchedule { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public double TimeRangeDays { get; set; } = 1.0;
    public string StorageContainer { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}
