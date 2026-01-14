using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;

public interface ITelemetryService
{
    Task<IEnumerable<QueryResultTransfer>> QueryTransfersAsync();
}
