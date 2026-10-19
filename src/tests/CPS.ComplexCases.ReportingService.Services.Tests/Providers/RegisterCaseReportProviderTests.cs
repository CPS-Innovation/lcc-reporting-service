using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests.Providers;

public class RegisterCaseReportProviderTests
{
    private readonly Mock<ILogger<RegisterCaseReportProvider>> _loggerMock;
    private readonly Mock<LogsQueryClient> _logsQueryClientMock;
    private readonly RegisterCaseReportProvider _provider;
    private const string WorkspaceId = "rac-workspace-id";
    private const string ExpectedHeader = "JourneyId,Username,AreaOrDivision,StartedAt,CompletedAt,DropOffScreen,Urn,Outcome";

    public RegisterCaseReportProviderTests()
    {
        _loggerMock = new Mock<ILogger<RegisterCaseReportProvider>>();
        _logsQueryClientMock = new Mock<LogsQueryClient>();
        _provider = new RegisterCaseReportProvider(_loggerMock.Object);
    }

    private void SetupQueryResults(
        IReadOnlyList<QueryResultRegisterCase> results,
        Action<string, string, QueryTimeRange>? callback = null)
    {
        var responseMock = new Mock<Response<IReadOnlyList<QueryResultRegisterCase>>>();
        responseMock.Setup(r => r.Value).Returns(results);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultRegisterCase>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, QueryTimeRange, LogsQueryOptions, CancellationToken>(
                (workspace, query, timeRange, _, _) => callback?.Invoke(workspace, query, timeRange))
            .ReturnsAsync(responseMock.Object);
    }

    private void SetupQueryException(Exception exception)
    {
        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultRegisterCase>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private static string[] GetLines(string content) =>
        content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void ReportKey_IsRegisterCase()
    {
        Assert.Equal("RegisterCase", _provider.ReportKey);
        Assert.Equal(RegisterCaseReportProvider.ReportKeyName, _provider.ReportKey);
    }

    [Fact]
    public void CronScheduleSetting_ResolvesFromReportConfiguration()
    {
        Assert.Equal("%Reports:RegisterCase:CronSchedule%", RegisterCaseReportProvider.CronScheduleSetting);
    }

    [Fact]
    public void GenerateFileName_UsesMonthFolderAndDatedFileName()
    {
        // Act
        var fileName = _provider.GenerateFileName();

        // Assert
        Assert.Matches(@"^\d{4}-\d{2}/RAC_Journey_Report_\d{4}-\d{2}-\d{2}\.csv$", fileName);

        var today = DateTime.UtcNow;
        Assert.StartsWith($"{today:yyyy-MM}/", fileName);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_PassesWorkspaceQueryToConfiguredWorkspace()
    {
        // Arrange
        string? capturedWorkspace = null;
        string? capturedQuery = null;
        SetupQueryResults([], (workspace, query, _) =>
        {
            capturedWorkspace = workspace;
            capturedQuery = query;
        });

        // Act
        await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Equal(WorkspaceId, capturedWorkspace);
        Assert.NotNull(capturedQuery);

        // Workspace-based table names
        Assert.Contains("AppEvents", capturedQuery);
        Assert.Contains("AppPageViews", capturedQuery);
        Assert.Contains("timestamp = TimeGenerated", capturedQuery);
        Assert.DoesNotContain("customEvents", capturedQuery);
        Assert.DoesNotContain("pageViews", capturedQuery);
        Assert.DoesNotContain("customDimensions", capturedQuery);

        // Filtering
        Assert.Contains("=~ \"Cps.RegisterCase\"", capturedQuery);
        Assert.Contains("\"JourneyStarted\", \"JourneyCancelled\", \"CaseRegistered\"", capturedQuery);
        Assert.Contains("pageName != \"Cancel Case Registration Confirmation\"", capturedQuery);
        Assert.Contains("by journeyId", capturedQuery);

        // Outcome logic
        Assert.Contains("\"Completed\"", capturedQuery);
        Assert.Contains("\"Cancelled\"", capturedQuery);
        Assert.Contains("\"Abandoned\"", capturedQuery);

        // Projection matches the domain model
        foreach (var property in typeof(QueryResultRegisterCase).GetProperties())
        {
            Assert.Contains($"{property.Name} = ", capturedQuery);
        }

        Assert.Contains("order by StartedAt desc", capturedQuery);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(0.5)]
    public async Task GenerateCsvContentAsync_UsesConfiguredTimeRange(double timeRangeInDays)
    {
        // Arrange
        QueryTimeRange? capturedTimeRange = null;
        SetupQueryResults([], (_, _, timeRange) => capturedTimeRange = timeRange);

        // Act
        await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, timeRangeInDays);

        // Assert
        Assert.NotNull(capturedTimeRange);
        Assert.Equal(TimeSpan.FromDays(timeRangeInDays), capturedTimeRange.Value.Duration);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WhenNoRecords_ReturnsEmptyAndLogsInformation()
    {
        // Arrange
        SetupQueryResults([]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Equal(string.Empty, content);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No Register a Case journey data found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesHeaderAndCompletedJourneyInColumnOrder()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase
            {
                JourneyId = "journey-1",
                Username = "jane.doe@cps.gov.uk",
                AreaOrDivision = "Humberside",
                StartedAt = DateTimeOffset.Parse("2026-10-14T09:15:30.123Z"),
                CompletedAt = DateTimeOffset.Parse("2026-10-14T09:42:01.500Z"),
                DropOffScreen = "",
                Urn = "45AA1234526",
                Outcome = "Completed"
            }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        var lines = GetLines(content);
        Assert.Equal(2, lines.Length);
        Assert.Equal(ExpectedHeader, lines[0]);
        Assert.Equal(
            "journey-1,jane.doe@cps.gov.uk,Humberside,2026-10-14T09:15:30.123Z,2026-10-14T09:42:01.500Z,,45AA1234526,Completed",
            lines[1]);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesAbandonedJourneyWithEmptyCompletionFields()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase
            {
                JourneyId = "journey-2",
                Username = "john.smith@cps.gov.uk",
                AreaOrDivision = "",
                StartedAt = DateTimeOffset.Parse("2026-10-14T10:00:00Z"),
                CompletedAt = null,
                DropOffScreen = "Defendant details",
                Urn = "",
                Outcome = "Abandoned"
            }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Equal(
            "journey-2,john.smith@cps.gov.uk,,2026-10-14T10:00:00.000Z,,Defendant details,,Abandoned",
            GetLines(content)[1]);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesNullFieldsAsEmptyColumns()
    {
        // Arrange
        SetupQueryResults([new QueryResultRegisterCase { JourneyId = "journey-3" }]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Equal("journey-3,,,,,,,", GetLines(content)[1]);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_ConvertsTimestampsToUtc()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase
            {
                JourneyId = "journey-4",
                StartedAt = new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.FromHours(1))
            }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Contains(",2026-10-14T10:00:00.000Z,", GetLines(content)[1]);
    }

    [Theory]
    [InlineData("North East, Yorkshire", "\"North East, Yorkshire\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("Line\nBreak", "\"Line\nBreak\"")]
    public async Task GenerateCsvContentAsync_EscapesValuesContainingCsvDelimiters(string value, string expected)
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase { JourneyId = "journey-5", AreaOrDivision = value, Outcome = "Completed" }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Contains($"journey-5,,{expected},,,,,Completed", content);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesOneLinePerJourneyInQueryOrder()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase { JourneyId = "j-3", Outcome = "Completed" },
            new QueryResultRegisterCase { JourneyId = "j-2", Outcome = "Cancelled" },
            new QueryResultRegisterCase { JourneyId = "j-1", Outcome = "Abandoned" }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        var lines = GetLines(content);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("j-3,", lines[1]);
        Assert.StartsWith("j-2,", lines[2]);
        Assert.StartsWith("j-1,", lines[3]);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Writing 3 Register a Case journeys")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_When403Forbidden_LogsAccessDeniedAndThrows()
    {
        // Arrange
        var forbiddenException = new RequestFailedException(403, "Forbidden");
        SetupQueryException(forbiddenException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0));

        Assert.Equal(403, exception.Status);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Access denied (403)")),
                forbiddenException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WhenOtherRequestFails_LogsStatusAndThrows()
    {
        // Arrange
        var requestFailedException = new RequestFailedException(500, "Server Error");
        SetupQueryException(requestFailedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0));

        Assert.Equal(500, exception.Status);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Request to Log Analytics failed")),
                requestFailedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WhenUnexpectedException_LogsErrorAndThrows()
    {
        // Arrange
        var generalException = new InvalidOperationException("Something went wrong");
        SetupQueryException(generalException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                generalException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WithNullClient_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.GenerateCsvContentAsync(null!, WorkspaceId, 1.0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateCsvContentAsync_WithInvalidWorkspaceId_ThrowsArgumentException(string? workspaceId)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, workspaceId!, 1.0));

        Assert.Equal("workspaceId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GenerateCsvContentAsync_WithInvalidTimeRange_ThrowsArgumentOutOfRangeException(double timeRangeInDays)
    {
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, timeRangeInDays));

        Assert.Equal("timeRangeInDays", exception.ParamName);
    }
}
