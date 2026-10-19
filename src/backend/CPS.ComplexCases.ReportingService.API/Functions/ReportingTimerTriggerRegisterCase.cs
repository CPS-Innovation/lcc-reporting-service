using System.Diagnostics;
using CPS.ComplexCases.ReportingService.Services;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CPS.ComplexCases.ReportingService.API.Functions;

public class ReportingTimerTriggerRegisterCase(ILogger<ReportingTimerTriggerRegisterCase> logger, IReportingService reportingService)
{
    private readonly ILogger<ReportingTimerTriggerRegisterCase> _logger = logger;
    private readonly IReportingService _reportingService = reportingService;

    [Function("ReportingTimerTriggerRegisterCase")]
    public async Task GenerateRegisterCaseReportsAsync(
        [TimerTrigger(RegisterCaseReportProvider.CronScheduleSetting)] TimerInfo myTimer)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Register a Case Report generation function executed at: {executionTime}", DateTime.UtcNow);

        try
        {
            await _reportingService.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName);
            _logger.LogInformation("Register a Case Reports generated successfully in {duration} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while generating Register a Case Reports.");
            throw;
        }
    }
}
