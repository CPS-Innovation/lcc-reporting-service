using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services
{
    public interface IQueryProcessor
    {
        Task<IEnumerable<QueryResultTransfer>> ProcessQueryTransfersAsync(string query);
    }
}