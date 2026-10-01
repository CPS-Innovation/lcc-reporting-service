using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Configuration;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Extensions.Options;

namespace CPS.ComplexCases.ReportingService.Services;

public class ReportingService : IReportingService
{
    private readonly ILogger<ReportingService> _logger;
    private readonly IEnumerable<IReportProvider> _reportProviders;
    private readonly IBlobStorageService _blobStorageService;
    private readonly LogsQueryClient _logsQueryClient;
    private readonly ReportsOptions _reportsOptions;

    public ReportingService(
        ILogger<ReportingService> logger,
        IEnumerable<IReportProvider> reportProviders,
        IBlobStorageService blobStorageService,
        LogsQueryClient logsQueryClient,
        IOptions<ReportsOptions> reportsOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportProviders = reportProviders ?? throw new ArgumentNullException(nameof(reportProviders));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _logsQueryClient = logsQueryClient ?? throw new ArgumentNullException(nameof(logsQueryClient));
        _reportsOptions = reportsOptions?.Value ?? throw new ArgumentNullException(nameof(reportsOptions));
    }

    public async Task ProcessReportAsync(string reportKey)
    {
        if (string.IsNullOrWhiteSpace(reportKey))
            throw new ArgumentException("Report key cannot be null or empty.", nameof(reportKey));

        try
        {
            var provider = _reportProviders.FirstOrDefault(p =>
                string.Equals(p.ReportKey, reportKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No report provider is registered for report key '{reportKey}'.");

            var config = FindConfig(reportKey)
                ?? throw new InvalidOperationException($"No configuration was found under Reports:{reportKey}.");

            if (!config.Enabled)
            {
                _logger.LogInformation("Report {ReportKey} is disabled and will not be generated.", reportKey);
                return;
            }

            if (string.IsNullOrWhiteSpace(config.WorkspaceId))
                throw new InvalidOperationException($"Reports:{reportKey}:WorkspaceId is missing or empty.");

            if (string.IsNullOrWhiteSpace(config.StorageContainer))
                throw new InvalidOperationException($"Reports:{reportKey}:StorageContainer is missing or empty.");

            var content = await provider.GenerateCsvContentAsync(_logsQueryClient, config.WorkspaceId, config.TimeRangeDays);

            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            await _blobStorageService.UploadBlobContentAsync(
                config.StorageContainer,
                BuildBlobPath(config.StoragePath, provider.GenerateFileName()),
                content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the report.");
            throw;
        }
    }

    private ReportConfig? FindConfig(string reportKey)
    {
        if (_reportsOptions.Reports.TryGetValue(reportKey, out var config))
        {
            return config;
        }

        return _reportsOptions.Reports
            .FirstOrDefault(entry => string.Equals(entry.Key, reportKey, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static string BuildBlobPath(string storagePath, string fileName)
    {
        var folder = storagePath?.Trim().Trim('/');

        return string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";
    }
}
