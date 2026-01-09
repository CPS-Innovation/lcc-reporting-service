using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class QueryProcessorTests : IDisposable
{
    private readonly Mock<ILogger<QueryProcessor>> _loggerMock;
    private readonly Mock<LogsQueryClient> _logsQueryClientMock;
    private readonly string _workspaceId = "test-workspace-id";
    private readonly QueryProcessor _queryProcessor;
    private string? _originalTimeRangeEnv;

    public QueryProcessorTests()
    {
        _loggerMock = new Mock<ILogger<QueryProcessor>>();
        _logsQueryClientMock = new Mock<LogsQueryClient>();

        _originalTimeRangeEnv = Environment.GetEnvironmentVariable("TimeRangeInDays");

        Environment.SetEnvironmentVariable("TimeRangeInDays", "7.0");

        _queryProcessor = new QueryProcessor(
            _loggerMock.Object,
            _logsQueryClientMock.Object,
            _workspaceId);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TimeRangeInDays", _originalTimeRangeEnv);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var expectedResults = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                CaseId = "CASE-001",
                Username = "user1@test.com",
                TransferDirection = "Upload",
                InitiatedTime = DateTimeOffset.UtcNow.AddHours(-2),
                CompletedTime = DateTimeOffset.UtcNow,
                DurationFormatted = "02:00:00",
                TotalFiles = 10,
                TransferredFiles = 10,
                ErrorFiles = 0,
                TransferSpeedMbps = 25.5
            },
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                CaseId = "CASE-002",
                Username = "user2@test.com",
                TransferDirection = "Download",
                InitiatedTime = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedTime = DateTimeOffset.UtcNow,
                DurationFormatted = "01:00:00",
                TotalFiles = 5,
                TransferredFiles = 4,
                ErrorFiles = 1,
                TransferSpeedMbps = 15.2
            }
        };

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(expectedResults);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        // Act
        var result = await _queryProcessor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        _logsQueryClientMock.Verify(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
            _workspaceId,
            query,
            It.IsAny<QueryTimeRange>(),
            It.IsAny<LogsQueryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithEmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var query = string.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _queryProcessor.ProcessQueryTransfersAsync(query));

        Assert.Equal("query", exception.ParamName);
        Assert.Contains("Query cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WhenRequestFails_LogsErrorAndThrows()
    {
        // Arrange
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var requestException = new RequestFailedException(404, "Not Found");

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(requestException);

        // Act & Assert
        await Assert.ThrowsAsync<RequestFailedException>(
            () => _queryProcessor.ProcessQueryTransfersAsync(query));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Request to Application Insights failed")),
                It.IsAny<RequestFailedException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WhenGenericExceptionOccurs_LogsErrorAndThrows()
    {
        // Arrange
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var genericException = new InvalidOperationException("Unexpected error");

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(genericException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _queryProcessor.ProcessQueryTransfersAsync(query));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("An error occurred while processing the query transfers")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithEmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var emptyResults = new List<QueryResultTransfer>();

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(emptyResults);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        // Act
        var result = await _queryProcessor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("30")]
    [InlineData("1.5")]
    [InlineData("0.5")]
    public async Task ProcessQueryTransfersAsync_WithDifferentTimeRanges_UsesCorrectTimeRange(string timeRangeDays)
    {
        // Arrange
        Environment.SetEnvironmentVariable("TimeRangeInDays", timeRangeDays);
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var expectedResults = new List<QueryResultTransfer>();

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(expectedResults);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        // Act
        var result = await _queryProcessor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(result);
        _logsQueryClientMock.Verify(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
            _workspaceId,
            query,
            It.IsAny<QueryTimeRange>(),
            It.IsAny<LogsQueryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("-5")]
    [InlineData("0")]
    public async Task ProcessQueryTransfersAsync_WithInvalidTimeRangeEnv_ThrowsInvalidOperationException(string? invalidTimeRange)
    {
        // Arrange
        Environment.SetEnvironmentVariable("TimeRangeInDays", invalidTimeRange);
        var query = "AppEvents | where TimeGenerated > ago(7d)";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _queryProcessor.ProcessQueryTransfersAsync(query));

        Assert.Contains("TimeRangeInDays", exception.Message);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_CallsQueryWorkspaceWithCorrectParameters()
    {
        // Arrange
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var expectedResults = new List<QueryResultTransfer>();
        QueryTimeRange? capturedTimeRange = null;

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(expectedResults);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, QueryTimeRange, LogsQueryOptions, CancellationToken>(
                (ws, q, tr, opts, ct) => capturedTimeRange = tr)
            .ReturnsAsync(responseMock.Object);

        // Act
        await _queryProcessor.ProcessQueryTransfersAsync(query);

        // Assert
        _logsQueryClientMock.Verify(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
            _workspaceId,
            query,
            It.IsAny<QueryTimeRange>(),
            It.IsAny<LogsQueryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}