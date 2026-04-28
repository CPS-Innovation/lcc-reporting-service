namespace CPS.ComplexCases.ReportingService.Domain.Models;

public class QueryResultTransfer
{
    public Guid TransferId { get; set; }
    public string? CaseId { get; set; }
    public string? UserName { get; set; }
    public string? TransferDirection { get; set; }
    public DateTimeOffset? TransferCreated { get; set; }
    public DateTimeOffset? TransferCompleted { get; set; }
    public string? Status { get; set; }
    public string? TotalDataSize { get; set; }
    public int? TransferredFiles { get; set; }
    public int? ErrorFiles { get; set; }
}
