using Azure.Monitor.Query;

namespace CPS.ComplexCases.ReportingService.Services.Providers;

public interface IReportProvider
{
    string ReportKey { get; }
    string GenerateFileName();

    /// <summary>
    /// Runs the report's query and serialises the results. Returns an empty string when the
    /// query yields no records, signalling that nothing should be uploaded.
    /// </summary>
    Task<string> GenerateCsvContentAsync(LogsQueryClient client, string workspaceId, double timeRangeInDays);
}
