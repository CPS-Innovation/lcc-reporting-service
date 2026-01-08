using Azure;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;

public class TelemetryService(ILogger<TelemetryService> logger, IQueryProcessor queryProcessor) : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger = logger;
    private readonly IQueryProcessor _queryProcessor = queryProcessor;

    public async Task<IEnumerable<QueryResultTransfer>> QueryTransfersAsync()
    {
        string query = @"
        let initiated = AppEvents
            | where Name == 'ActivityLogTelemetry'
            | extend actionType = tostring(Properties.actionType)
            | where actionType == 'TRANSFER_INITIATED'
            | extend transferId = tostring(Properties.transferId)
            | project
                initiatedTime = TimeGenerated,
                transferId,
                caseId = tostring(Properties.caseId),
                userName = tostring(Properties.userName),
                sourcePath = tostring(Properties.sourcePath),
                destinationPath = tostring(Properties.destinationPath),
                transferDirection = tostring(Properties.transferDirection);
        let completed = AppEvents
            | where Name == 'ActivityLogTelemetry'
            | extend actionType = tostring(Properties.actionType)
            | where actionType in ('TRANSFER_COMPLETED', 'TRANSFER_FAILED')
            | extend transferId = tostring(Properties.transferId)
            | extend
                totalBytes = todouble(Measurements.totalBytes),
                totalFiles = todouble(Measurements.totalFiles),
                transferredFiles = todouble(Measurements.transferredFiles),
                errorFiles = todouble(Measurements.errorFiles)
            | where totalBytes > 0
            | project
                completedTime = TimeGenerated,
                transferId,
                actionType,
                totalBytes,
                totalFiles,
                transferredFiles,
                errorFiles;
        initiated
        | join kind=inner completed on transferId
        | extend durationSeconds = datetime_diff('second', completedTime, initiatedTime)
        | project
            TransferId = transferId,
            CaseId = caseId,
            Username = userName,
            TransferDirection = transferDirection,
            InitiatedTime = initiatedTime,
            CompletedTime = completedTime,
            DurationSeconds = durationSeconds,
            DurationFormatted = strcat(durationSeconds / 60, 'm ', durationSeconds % 60, 's'),
            TotalFiles = totalFiles,
            TransferredFiles = transferredFiles,
            ErrorFiles = errorFiles,
            TransferSpeedMbps = iff(durationSeconds > 0, round((totalBytes / 1024.0 / 1024.0) / durationSeconds, 3), 0.0)
        | order by InitiatedTime desc
    ";
        try
        {
            var result = await _queryProcessor.ProcessQueryTransfersAsync(query);
            return result;
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status403Forbidden)
        {
            _logger.LogError(ex, "Access denied (403) when querying transfers. Please check permissions.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while querying transfers.");
            return new List<QueryResultTransfer>();
        }
    }
}