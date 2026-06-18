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
                UserName = "testuser",
                TransferDirection = "Egress -> NetApp",
                TransferCreated = DateTime.UtcNow.AddHours(-1),
                TransferCompleted = DateTime.UtcNow,
                TransferredFiles = 10,
                ErrorFiles = 0,
                TotalDataSize = "1.93 GB",
                Status = "Success"
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

        // Source and join structure
        Assert.Contains("AppEvents", capturedQuery);
        Assert.Contains("TRANSFER_INITIATED", capturedQuery);
        Assert.Contains("TRANSFER_COMPLETED", capturedQuery);
        Assert.DoesNotContain("TRANSFER_FAILED", capturedQuery);
        Assert.Contains("join kind=leftouter", capturedQuery);

        // Completion-aware status logic
        Assert.Contains("isnotempty(TransferCompleted)", capturedQuery);
        Assert.Contains("'Success'", capturedQuery);
        Assert.Contains("'Partial'", capturedQuery);

        // TotalDataSize projection
        Assert.Contains("TotalDataSize", capturedQuery);

        // Final projected column list
        Assert.Contains("TransferId", capturedQuery);
        Assert.Contains("TransferCreated", capturedQuery);
        Assert.Contains("TransferCompleted", capturedQuery);
        Assert.Contains("Status", capturedQuery);
        Assert.Contains("TransferDirection", capturedQuery);
        Assert.Contains("UserName", capturedQuery);
        Assert.Contains("CaseId", capturedQuery);
        Assert.Contains("TransferredFiles", capturedQuery);
        Assert.Contains("ErrorFiles", capturedQuery);

        // Ordering
        Assert.Contains("order by TransferCreated desc", capturedQuery);
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
    public async Task QueryTransfersAsync_WhenOtherRequestFailedException_LogsErrorAndThrows()
    {
        // Arrange
        var otherException = new RequestFailedException(
            StatusCodes.Status500InternalServerError,
            "Server Error");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(otherException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _telemetryService.QueryTransfersAsync());

        Assert.Equal(StatusCodes.Status500InternalServerError, exception.Status);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                It.IsAny<RequestFailedException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryTransfersAsync_WhenGeneralException_LogsErrorAndThrows()
    {
        // Arrange
        var generalException = new InvalidOperationException("Something went wrong");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(generalException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _telemetryService.QueryTransfersAsync());

        Assert.Equal("Something went wrong", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                It.IsAny<InvalidOperationException>(),
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
    public async Task QueryTransfersAsync_WhenNullReferenceException_LogsErrorAndThrows()
    {
        // Arrange
        var nullRefException = new NullReferenceException("Null reference encountered");

        _queryProcessorMock
            .Setup(x => x.ProcessQueryTransfersAsync(It.IsAny<string>()))
            .ThrowsAsync(nullRefException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NullReferenceException>(
            () => _telemetryService.QueryTransfersAsync());

        Assert.Equal("Null reference encountered", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                It.IsAny<NullReferenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}