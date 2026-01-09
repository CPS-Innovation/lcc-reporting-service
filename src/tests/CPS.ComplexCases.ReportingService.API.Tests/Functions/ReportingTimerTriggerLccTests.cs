using Moq;
using CPS.ComplexCases.ReportingService.API.Functions;
using Microsoft.Extensions.Logging;
using CPS.ComplexCases.ReportingService.Services;
using Microsoft.Azure.Functions.Worker;

namespace CPS.ComplexCases.ReportingService.API.Tests.Functions;

public class ReportingTimerTriggerLccTests
{
    private readonly Mock<ILogger<ReportingTimerTriggerLcc>> _loggerMock;
    private readonly Mock<IReportingService> _reportingServiceMock;
    private readonly ReportingTimerTriggerLcc _function;

    public ReportingTimerTriggerLccTests()
    {
        _loggerMock = new Mock<ILogger<ReportingTimerTriggerLcc>>();
        _reportingServiceMock = new Mock<IReportingService>();
        _function = new ReportingTimerTriggerLcc(_loggerMock.Object, _reportingServiceMock.Object);
    }

    [Fact]
    public async Task GenerateLccReportsAsync_Success()
    {
        // Arrange
        _reportingServiceMock
            .Setup(service => service.ProcessReportAsync())
            .Returns(Task.CompletedTask);

        var timerInfo = new TimerInfo
        {
            ScheduleStatus = new ScheduleStatus
            {
                Last = DateTime.Now.AddMinutes(-5),
                Next = DateTime.Now.AddMinutes(5)
            },
            IsPastDue = false
        };

        // Act
        await _function.GenerateLccReportsAsync(timerInfo);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("LCC Report generation function executed at:")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("LCC Reports generated successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _reportingServiceMock.Verify(r => r.ProcessReportAsync(), Times.Once);
    }

    [Fact]
    public async Task GenerateLccReportsAsync_Exception()
    {
        // Arrange
        string exceptionMessage = "Simulated exception during LCC report generation";
        _reportingServiceMock
            .Setup(service => service.ProcessReportAsync())
            .ThrowsAsync(new Exception(exceptionMessage));

        var timerInfo = new TimerInfo
        {
            ScheduleStatus = new ScheduleStatus
            {
                Last = DateTime.Now.AddMinutes(-5),
                Next = DateTime.Now.AddMinutes(5)
            },
            IsPastDue = false
        };

        // Act
        await _function.GenerateLccReportsAsync(timerInfo);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("LCC Report generation function executed at:")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error occurred while generating LCC Reports")),
                It.Is<Exception>(ex => ex.Message == exceptionMessage),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _reportingServiceMock.Verify(r => r.ProcessReportAsync(), Times.Once);
    }
}