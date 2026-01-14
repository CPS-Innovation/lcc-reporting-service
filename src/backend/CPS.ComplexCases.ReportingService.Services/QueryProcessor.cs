using System.Globalization;
using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;


public class QueryProcessor(ILogger<QueryProcessor> logger, LogsQueryClient logsQueryClient, string workspaceId) : IQueryProcessor
{
    private readonly ILogger<QueryProcessor> _logger = logger;
    private readonly LogsQueryClient _logsQueryClient = logsQueryClient;
    private readonly string _workspaceId = workspaceId;
    private const string TimeRangeInDaysKey = "TimeRangeInDays";

    public async Task<IEnumerable<QueryResultTransfer>> ProcessQueryTransfersAsync(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));
        }

        double timeRangeDays = GetValidatedTimeRangeInDays();

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

    /// <summary>
    /// Retrieves and validates the time range in days from the environment variable.
    /// </summary>
    /// <returns>The validated time range in days.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the environment variable 'TimeRangeInDays' is not set, is empty, or contains an invalid value.
    /// </exception>
    private static double GetValidatedTimeRangeInDays()
    {
        string? timeRangeEnv = Environment.GetEnvironmentVariable(TimeRangeInDaysKey)?.Trim();
        if (string.IsNullOrEmpty(timeRangeEnv) ||
            !double.TryParse(timeRangeEnv, NumberStyles.Float, CultureInfo.InvariantCulture, out double timeRangeDays) ||
            timeRangeDays <= 0)
        {
            throw new InvalidOperationException($"The environment variable '{TimeRangeInDaysKey}' is not set, is empty, or contains an invalid value.");
        }

        return timeRangeDays;
    }
}