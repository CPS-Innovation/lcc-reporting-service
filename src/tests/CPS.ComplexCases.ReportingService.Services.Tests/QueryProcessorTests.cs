using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class QueryProcessorTests
{
    private readonly Mock<ILogger<QueryProcessor>> _loggerMock;
    private readonly Mock<LogsQueryClient> _logsQueryClientMock;
    private readonly string _workspaceId = "test-workspace-id";

    public QueryProcessorTests()
    {
        _loggerMock = new Mock<ILogger<QueryProcessor>>();
        _logsQueryClientMock = new Mock<LogsQueryClient>();
    }

    private QueryProcessor CreateProcessor(double timeRangeInDays = 7.0)
        => new(
            _loggerMock.Object,
            _logsQueryClientMock.Object,
            _workspaceId,
            timeRangeInDays);

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var processor = CreateProcessor();
        var query = "AppEvents | where TimeGenerated > ago(7d)";

        var expectedResults = new List<QueryResultTransfer>
        {
            new()
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
                TotalMegaBytesTransferred = 183.6,
                TransferSpeedMbps = 25.5
            },
            new()
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
                TotalMegaBytesTransferred = 15.2,
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
        var result = await processor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());

        _logsQueryClientMock.Verify(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
            _workspaceId,
            query,
            It.IsAny<QueryTimeRange>(),
            It.IsAny<LogsQueryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithEmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => processor.ProcessQueryTransfersAsync(string.Empty));

        Assert.Equal("query", exception.ParamName);
        Assert.Contains("Query cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WhenRequestFails_LogsErrorAndThrows()
    {
        // Arrange
        var processor = CreateProcessor();
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var exception = new RequestFailedException(404, "Not Found");

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsAsync<RequestFailedException>(
            () => processor.ProcessQueryTransfersAsync(query));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Request to Application Insights failed")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WhenGenericExceptionOccurs_LogsErrorAndThrows()
    {
        // Arrange
        var processor = CreateProcessor();
        var query = "AppEvents | where TimeGenerated > ago(7d)";
        var exception = new InvalidOperationException("Unexpected error");

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessQueryTransfersAsync(query));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("An error occurred while processing the query transfers")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueryTransfersAsync_WithEmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var processor = CreateProcessor();
        var query = "AppEvents | where TimeGenerated > ago(7d)";

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(Array.Empty<QueryResultTransfer>());

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        // Act
        var result = await processor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(1.5)]
    [InlineData(0.5)]
    public async Task ProcessQueryTransfersAsync_WithDifferentTimeRanges_UsesCorrectTimeRange(double timeRangeDays)
    {
        // Arrange
        var processor = CreateProcessor(timeRangeDays);
        var query = "AppEvents | where TimeGenerated > ago(7d)";

        QueryTimeRange? capturedTimeRange = null;

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(Array.Empty<QueryResultTransfer>());

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                _workspaceId,
                query,
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, QueryTimeRange, LogsQueryOptions, CancellationToken>(
                (_, _, tr, _, _) => capturedTimeRange = tr)
            .ReturnsAsync(responseMock.Object);

        // Act
        await processor.ProcessQueryTransfersAsync(query);

        // Assert
        Assert.NotNull(capturedTimeRange);
        Assert.Equal(TimeSpan.FromDays(timeRangeDays), capturedTimeRange);
    }
}
