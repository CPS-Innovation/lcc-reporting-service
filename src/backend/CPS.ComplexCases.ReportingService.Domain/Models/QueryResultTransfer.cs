namespace CPS.ComplexCases.ReportingService.Domain.Models;

public class QueryResultTransfer
{
    public Guid TransferId { get; set; }
    public string? CaseId { get; set; }
    public string? Username { get; set; }
    public string? TransferDirection { get; set; }
    public DateTimeOffset? InitiatedTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
    public string? DurationFormatted { get; set; }
    public int? TotalFiles { get; set; }
    public int? TransferredFiles { get; set; }
    public int? ErrorFiles { get; set; }
    public double? TotalMegaBytesTransferred { get; set; }
    public double? TransferSpeedMbps { get; set; }
    public string TransferStatus =>
        (ErrorFiles ?? 0) == 0 ? "Success" :
        (TransferredFiles ?? 0) == 0 ? "Failed" :
        "Partial";
}