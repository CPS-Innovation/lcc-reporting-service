using System.Globalization;
using System.Text;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;

public class ReportingService(ILogger<ReportingService> logger, ITelemetryService telemetryService, IBlobStorageService blobStorageService) : IReportingService
{
    private readonly ILogger<ReportingService> _logger = logger;
    private readonly ITelemetryService _telemetryService = telemetryService;
    private readonly IBlobStorageService _blobStorageService = blobStorageService;

    public async Task ProcessReportAsync()
    {
        try
        {
            string? containerName = Environment.GetEnvironmentVariable("BlobContainerNameReporting");
            if (string.IsNullOrEmpty(containerName))
            {
                _logger.LogWarning("BlobContainerNameReporting is not configured.");
                throw new InvalidOperationException("BlobContainerNameReporting is not configured.");
            }

            var sb = new StringBuilder(CreateFileHeader());

            var transfers = await _telemetryService.QueryTransfersAsync();

            if (transfers == null || !transfers.Any())
            {
                _logger.LogInformation("No transfer data found for the specified time range.");
                return;
            }

            foreach (var transfer in transfers)
            {
                _logger.LogInformation("Processing transfer: {TransferId}", transfer.TransferId);
                sb.AppendLine(AppendFileLine(transfer));
            }

            await _blobStorageService.UploadBlobContentAsync(containerName, GenerateFileNameInFolder(), sb.ToString());

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the report.");
            throw;
        }
    }

    private static string GenerateFileNameInFolder()
    {
        DateTime now = DateTime.UtcNow;

        // Specify a folder name using the year and month
        string folderName = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        // Generate the file name with the current date under the specified folder
        string fileName = $"{folderName}/LCC_Transfer_Report_{now:yyyy-MM-dd}.csv";

        return fileName;
    }

    private static string CreateFileHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TransferId, CaseId, Username, TransferDirection, StartedTime, CompletedTime, Duration, TotalFiles, TransferredFiled, ErrorFiles, AverageTransferSpeedMbps");
        return sb.ToString();
    }

    private static string AppendFileLine(QueryResultTransfer transfer)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}",
            transfer.TransferId,
            transfer.CaseId,
            transfer.Username,
            transfer.TransferDirection,
            transfer.InitiatedTime,
            transfer.CompletedTime,
            transfer.DurationFormatted,
            transfer.TotalFiles,
            transfer.TransferredFiles,
            transfer.ErrorFiles,
            transfer.TransferSpeedMbps
        );
    }
}
