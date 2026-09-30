namespace CPS.ComplexCases.ReportingService.Domain.Configuration;

public class ReportsOptions
{
    public Dictionary<string, ReportConfig> Reports { get; set; } = new();
}
