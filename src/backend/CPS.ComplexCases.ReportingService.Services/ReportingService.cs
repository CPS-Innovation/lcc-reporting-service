using System.Globalization;
using System.Text;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services;

public class ReportingService : IReportingService
{
    private readonly ILogger<ReportingService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public ReportingService(
        ILogger<ReportingService> logger,
        ITelemetryService telemetryService,
        IBlobStorageService blobStorageService,
        string containerName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));

        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name cannot be null or empty.", nameof(containerName));

        _containerName = containerName;
    }

    public async Task ProcessReportAsync()
    {
        try
        {
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

            await _blobStorageService.UploadBlobContentAsync(_containerName, GenerateFileNameInFolder(), sb.ToString());
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
        sb.AppendLine("TransferId, CaseId, Username, TransferDirection, StartedTime, CompletedTime, Duration, TotalFiles, TransferredFiles, ErrorFiles, TotalMegaBytesTransferred, AverageTransferSpeedMbps, TransferStatus");
        return sb.ToString();
    }

    private static string AppendFileLine(QueryResultTransfer transfer)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
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
            transfer.TotalMegaBytesTransferred,
            transfer.TransferSpeedMbps,
            transfer.TransferStatus
        );
    }
}