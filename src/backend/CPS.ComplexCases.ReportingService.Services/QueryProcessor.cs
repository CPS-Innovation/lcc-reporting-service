using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;


public class QueryProcessor(ILogger<QueryProcessor> logger, LogsQueryClient logsQueryClient, string workspaceId, double timeRangeInDays) : IQueryProcessor
{
    private readonly ILogger<QueryProcessor> _logger = logger;
    private readonly LogsQueryClient _logsQueryClient = logsQueryClient;
    private readonly string _workspaceId = workspaceId;
    private readonly double _timeRangeInDays = timeRangeInDays;

    public async Task<IEnumerable<QueryResultTransfer>> ProcessQueryTransfersAsync(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));
        }

        double timeRangeDays = _timeRangeInDays;

        try
        {
            var timeRange = new QueryTimeRange(TimeSpan.FromDays(timeRangeDays));

            var response = await _logsQueryClient.QueryWorkspaceAsync<QueryResultTransfer>(_workspaceId, query, timeRange);

            return response.Value.ToList();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Request to Application Insights failed with status {status}.", ex.Status);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the query transfers.");
            throw;
        }
    }
}