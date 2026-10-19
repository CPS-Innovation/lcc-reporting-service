using System.Reflection;
using Moq;
using CPS.ComplexCases.ReportingService.API.Functions;
using Microsoft.Extensions.Logging;
using CPS.ComplexCases.ReportingService.Services;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Azure.Functions.Worker;

namespace CPS.ComplexCases.ReportingService.API.Tests.Functions;

public class ReportingTimerTriggerRegisterCaseTests
{
    private readonly Mock<ILogger<ReportingTimerTriggerRegisterCase>> _loggerMock;
    private readonly Mock<IReportingService> _reportingServiceMock;
    private readonly ReportingTimerTriggerRegisterCase _function;

    public ReportingTimerTriggerRegisterCaseTests()
    {
        _loggerMock = new Mock<ILogger<ReportingTimerTriggerRegisterCase>>();
        _reportingServiceMock = new Mock<IReportingService>();
        _function = new ReportingTimerTriggerRegisterCase(_loggerMock.Object, _reportingServiceMock.Object);
    }

    private static TimerInfo CreateTimerInfo() => new()
    {
        ScheduleStatus = new ScheduleStatus
        {
            Last = DateTime.Now.AddMinutes(-5),
            Next = DateTime.Now.AddMinutes(5)
        },
        IsPastDue = false
    };

    [Fact]
    public void GenerateRegisterCaseReportsAsync_BindsScheduleToReportConfiguration()
    {
        // Arrange
        var method = typeof(ReportingTimerTriggerRegisterCase)
            .GetMethod(nameof(ReportingTimerTriggerRegisterCase.GenerateRegisterCaseReportsAsync))!;

        // Act
        var function = method.GetCustomAttribute<FunctionAttribute>();
        var trigger = method.GetParameters().Single().GetCustomAttribute<TimerTriggerAttribute>();

        // Assert
        Assert.NotNull(function);
        Assert.Equal("ReportingTimerTriggerRegisterCase", function.Name);
        Assert.NotNull(trigger);
        Assert.Equal("%Reports:RegisterCase:CronSchedule%", trigger.Schedule);
    }

    [Fact]
    public async Task GenerateRegisterCaseReportsAsync_Success()
    {
        // Arrange
        _reportingServiceMock
            .Setup(service => service.ProcessReportAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _function.GenerateRegisterCaseReportsAsync(CreateTimerInfo());

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Register a Case Report generation function executed at:")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Register a Case Reports generated successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _reportingServiceMock.Verify(
            r => r.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName),
            Times.Once);
        _reportingServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateRegisterCaseReportsAsync_Exception_RethrowsException()
    {
        // Arrange
        const string exceptionMessage = "Simulated exception during Register a Case report generation";

        _reportingServiceMock
            .Setup(service => service.ProcessReportAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception(exceptionMessage));

        // Act
        var exception = await Assert.ThrowsAsync<Exception>(
            () => _function.GenerateRegisterCaseReportsAsync(CreateTimerInfo()));

        // Assert
        Assert.Equal(exceptionMessage, exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error occurred while generating Register a Case Reports")),
                It.Is<Exception>(ex => ex.Message == exceptionMessage),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _reportingServiceMock.Verify(
            r => r.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName),
            Times.Once);
    }
}
