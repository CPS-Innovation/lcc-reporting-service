using System.Diagnostics;
using CPS.ComplexCases.ReportingService.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CPS.ComplexCases.ReportingService.API.Functions;

public class ReportingTimerTriggerLcc(ILogger<ReportingTimerTriggerLcc> logger, IReportingService reportingService)
{
    private readonly ILogger<ReportingTimerTriggerLcc> _logger = logger;
    private readonly IReportingService _reportingService = reportingService;

    [Function("ReportingTimerTriggerLcc")]
    public async Task GenerateLccReportsAsync([TimerTrigger("%TimerTriggerLccSchedule%")] TimerInfo myTimer)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("LCC Report generation function executed at: {executionTime}", DateTime.Now);

        try
        {
            await _reportingService.ProcessReportAsync();
            _logger.LogInformation("LCC Reports generated successfully in {duration} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while generating LCC Reports.");
        }
    }
}
