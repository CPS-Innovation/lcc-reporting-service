using Azure;
using CPS.ComplexCases.ReportingService.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class TelemetryServiceTests
{
    private readonly Mock<ILogger<TelemetryService>> _loggerMock;
    private readonly Mock<IQueryProcessor> _queryProcessorMock;
    private readonly TelemetryService _telemetryService;

    public TelemetryServiceTests()
    {
        _loggerMock = new Mock<ILogger<TelemetryService>>();
        _queryProcessorMock = new Mock<IQueryProcessor>();
        _telemetryService = new TelemetryService(
            _loggerMock.Object,
            _queryProcessorMock.Object);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenSuccessful_ReturnsQueryResults()
    {
        // Arrange
        var expectedResults = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                CaseId = "case-456",
                Username = "testuser",
                TransferDirection = "UPLOAD",
                InitiatedTime = DateTime.UtcNow.AddHours(-1),
                CompletedTime = DateTime.UtcNow,
                TotalFiles = 10,
                TransferredFiles = 10,
                ErrorFiles = 0,
                TransferSpeedMbps = 5.5
            }
        };

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedResults.Count, result.Count());
        Assert.Equal(expectedResults.First().TransferId, result.First().TransferId);

        _queryProcessorMock.Verify(
            x => x.ProcessQueryTransfersAsync(It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenSuccessful_PassesCorrectQueryToProcessor()
    {
        // Arrange
        string? capturedQuery = null;
        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .Callback<string>(query => capturedQuery = query)
            .ReturnsAsync(new List<QueryResultTransfer>());

        // Act
        await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(capturedQuery);
        Assert.Contains("AppEvents", capturedQuery);
        Assert.Contains("TRANSFER_INITIATED", capturedQuery);
        Assert.Contains("TRANSFER_COMPLETED", capturedQuery);
        Assert.Contains("TRANSFER_FAILED", capturedQuery);
        Assert.Contains("join kind=inner", capturedQuery);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenEmptyResults_ReturnsEmptyCollection()
    {
        // Arrange
        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<QueryResultTransfer>());

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryTransfersAsync_When403Forbidden_LogsErrorAndThrows()
    {
        // Arrange
        var forbiddenException = new RequestFailedException(
            StatusCodes.Status403Forbidden,
            "Forbidden");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(forbiddenException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _telemetryService.QueryTransfersAsync());

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Access denied (403)")),
                It.IsAny<RequestFailedException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenOtherRequestFailedException_LogsErrorAndReturnsEmpty()
    {
        // Arrange
        var otherException = new RequestFailedException(
            StatusCodes.Status500InternalServerError,
            "Server Error");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(otherException);

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenGeneralException_LogsErrorAndReturnsEmpty()
    {
        // Arrange
        var generalException = new InvalidOperationException("Something went wrong");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(generalException);

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenMultipleResults_ReturnsAllResults()
    {
        var transferId1 = Guid.NewGuid();
        var transferId2 = Guid.NewGuid();
        var transferId3 = Guid.NewGuid();
        // Arrange
        var expectedResults = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = transferId1, CaseId = "case-1" },
            new QueryResultTransfer { TransferId = transferId2, CaseId = "case-2" },
            new QueryResultTransfer { TransferId = transferId3, CaseId = "case-3" }
        };

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count());
        Assert.Contains(result, r => r.TransferId == transferId1);
        Assert.Contains(result, r => r.TransferId == transferId2);
        Assert.Contains(result, r => r.TransferId == transferId3);
    }

    [Fact]
    public async Task QueryTransfersAsync_CallsQueryProcessorExactlyOnce()
    {
        // Arrange
        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<QueryResultTransfer>());

        // Act
        await _telemetryService.QueryTransfersAsync();

        // Assert
        _queryProcessorMock.Verify(
            x => x.ProcessQueryTransfersAsync(It.IsAny<string>()),
            Times.Once);

        _queryProcessorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenNullResult_ReturnsEmptyOnException()
    {
        // Arrange
        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(new NullReferenceException());

        // Act
        var result = await _telemetryService.QueryTransfersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}