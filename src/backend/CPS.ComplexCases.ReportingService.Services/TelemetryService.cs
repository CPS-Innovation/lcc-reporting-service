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
            | extend transferDirectionRaw = tostring(Properties.transferDirection)
            | extend transferDirection = case(
                transferDirectionRaw == 'EgressToNetApp', 'Egress -> NetApp',
                transferDirectionRaw == 'NetAppToEgress', 'NetApp -> Egress',
                transferDirectionRaw == 'NetAppToNetApp', 'NetApp -> NetApp',
                transferDirectionRaw)
            | project
                TransferId = transferId,
                TransferCreated = TimeGenerated,
                UserName = tostring(Properties.userName),
                CaseId = tostring(Properties.caseId),
                TransferDirection = transferDirection;
        let completed = AppEvents
            | where Name == 'ActivityLogTelemetry'
            | extend actionType = tostring(Properties.actionType)
            | where actionType == 'TRANSFER_COMPLETED'
            | extend transferId = tostring(Properties.transferId)
            | extend
                totalBytes = todouble(Measurements.totalBytes),
                totalFiles = toint(Measurements.totalFiles),
                transferredFiles = toint(Measurements.transferredFiles),
                errorFiles = toint(Measurements.errorFiles)
            | project
                TransferId = transferId,
                TransferCompleted = TimeGenerated,
                TotalFiles = totalFiles,
                TransferredFiles = transferredFiles,
                ErrorFiles = errorFiles,
                TotalBytes = totalBytes;
        initiated
        | join kind=leftouter completed on TransferId
        | extend
            Status = case(
                isnotempty(TransferCompleted) and ErrorFiles == 0, 'Success',
                isnotempty(TransferCompleted) and ErrorFiles > 0 and TransferredFiles > 0, 'Partial',
                'Failed'),
            TotalDataSize = case(
                TotalBytes >= 1073741824, strcat(round(TotalBytes / 1073741824.0, 2), ' GB'),
                TotalBytes >= 1048576, strcat(round(TotalBytes / 1048576.0, 2), ' MB'),
                TotalBytes >= 1024, strcat(round(TotalBytes / 1024.0, 2), ' KB'),
                strcat(round(TotalBytes, 0), ' Bytes'))
        | project
            TransferId,
            TransferCreated,
            TransferCompleted,
            Status,
            TransferDirection,
            UserName,
            CaseId,
            TotalDataSize,
            TransferredFiles,
            ErrorFiles
        | order by TransferCreated desc
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
            throw;
        }
    }
}