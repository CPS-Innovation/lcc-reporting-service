using System.Globalization;
using System.Text;
using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Configuration;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services.Providers;

public class TransferMaterialReportProvider(ILogger<TransferMaterialReportProvider> logger) : IReportProvider
{
    public const string ReportKeyName = "TransferMaterial";

    /// <summary>
    /// Binding expression for this report's TimerTrigger. The Functions host resolves it against
    /// the same configuration value that <see cref="ReportConfig.CronSchedule"/> is bound from.
    /// </summary>
    public const string CronScheduleSetting = $"%Reports:{ReportKeyName}:CronSchedule%";

    private readonly ILogger<TransferMaterialReportProvider> _logger = logger;

    public string ReportKey => ReportKeyName;

    public string GenerateFileName()
    {
        DateTime now = DateTime.UtcNow;
        // Specify a folder name using the year and month
        string folderName = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        // Generate the file name with the current date under the specified folder
        string fileName = $"{folderName}/LCC_Transfer_Report_{now:yyyy-MM-dd}.csv";
        return fileName;
    }

    public async Task<string> GenerateCsvContentAsync(LogsQueryClient client, string workspaceId, double timeRangeInDays)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace id cannot be null or empty.", nameof(workspaceId));
        }

        if (timeRangeInDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeRangeInDays), "Time range in days must be greater than zero.");
        }

        var transfers = await QueryTransfersAsync(client, workspaceId, timeRangeInDays);

        if (transfers == null || !transfers.Any())
        {
            _logger.LogInformation("No transfer data found for the specified time range.");
            return string.Empty;
        }

        var sb = new StringBuilder(CreateFileHeader());

        foreach (var transfer in transfers)
        {
            _logger.LogInformation("Processing transfer: {TransferId}", transfer.TransferId);
            sb.AppendLine(AppendFileLine(transfer));
        }

        return sb.ToString();
    }

    private async Task<IEnumerable<QueryResultTransfer>> QueryTransfersAsync(LogsQueryClient client, string workspaceId, double timeRangeInDays)
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
            var timeRange = new QueryTimeRange(TimeSpan.FromDays(timeRangeInDays));

            var response = await client.QueryWorkspaceAsync<QueryResultTransfer>(workspaceId, query, timeRange);

            return response.Value.ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status403Forbidden)
        {
            _logger.LogError(ex, "Access denied (403) when querying transfers. Please check permissions.");
            throw;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Request to Application Insights failed with status {status}.", ex.Status);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while querying transfers.");
            throw;
        }
    }

    private static string CreateFileHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TransferId, TransferCreated, TransferCompleted, Status, TransferDirection, UserName, CaseId, TotalDataSize, TransferredFiles, ErrorFiles");
        return sb.ToString();
    }

    private static string AppendFileLine(QueryResultTransfer transfer)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
            transfer.TransferId,
            transfer.TransferCreated,
            transfer.TransferCompleted,
            transfer.Status,
            transfer.TransferDirection,
            transfer.UserName,
            transfer.CaseId,
            transfer.TotalDataSize,
            transfer.TransferredFiles,
            transfer.ErrorFiles
        );
    }
}
