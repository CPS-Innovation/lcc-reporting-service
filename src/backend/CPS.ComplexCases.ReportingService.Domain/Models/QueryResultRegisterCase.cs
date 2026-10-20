namespace CPS.ComplexCases.ReportingService.Domain.Models;

public class QueryResultRegisterCase
{
    public string? JourneyId { get; set; }
    public string? Username { get; set; }
    public string? AreaOrDivision { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DropOffScreen { get; set; }
    public string? Urn { get; set; }
    public string? Outcome { get; set; }
}
