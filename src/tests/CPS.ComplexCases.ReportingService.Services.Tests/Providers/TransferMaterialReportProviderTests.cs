using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Models;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests.Providers;

public class TransferMaterialReportProviderTests
{
    private readonly Mock<ILogger<TransferMaterialReportProvider>> _loggerMock;
    private readonly Mock<LogsQueryClient> _logsQueryClientMock;
    private readonly TransferMaterialReportProvider _provider;
    private const string WorkspaceId = "test-workspace-id";

    public TransferMaterialReportProviderTests()
    {
        _loggerMock = new Mock<ILogger<TransferMaterialReportProvider>>();
        _logsQueryClientMock = new Mock<LogsQueryClient>();
        _provider = new TransferMaterialReportProvider(_loggerMock.Object);
    }

    private void SetupQueryResults(IReadOnlyList<QueryResultTransfer> results)
    {
        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(results);

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);
    }

    private void SetupQueryException(Exception exception)
    {
        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    [Fact]
    public void ReportKey_IsTransferMaterial()
    {
        Assert.Equal("TransferMaterial", _provider.ReportKey);
        Assert.Equal(TransferMaterialReportProvider.ReportKeyName, _provider.ReportKey);
    }

    [Fact]
    public void GenerateFileName_UsesMonthFolderAndDatedFileName()
    {
        // Act
        var fileName = _provider.GenerateFileName();

        // Assert
        Assert.Matches(@"^\d{4}-\d{2}/LCC_Transfer_Report_\d{4}-\d{2}-\d{2}\.csv$", fileName);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_PassesExpectedQueryToWorkspace()
    {
        // Arrange
        string? capturedQuery = null;

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(Array.Empty<QueryResultTransfer>());

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, QueryTimeRange, LogsQueryOptions, CancellationToken>(
                (_, query, _, _, _) => capturedQuery = query)
            .ReturnsAsync(responseMock.Object);

        // Act
        await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

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

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(1.5)]
    [InlineData(0.5)]
    public async Task GenerateCsvContentAsync_UsesConfiguredTimeRange(double timeRangeInDays)
    {
        // Arrange
        QueryTimeRange? capturedTimeRange = null;

        var responseMock = new Mock<Response<IReadOnlyList<QueryResultTransfer>>>();
        responseMock.Setup(r => r.Value).Returns(Array.Empty<QueryResultTransfer>());

        _logsQueryClientMock
            .Setup(x => x.QueryWorkspaceAsync<QueryResultTransfer>(
                WorkspaceId,
                It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(),
                It.IsAny<LogsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, QueryTimeRange, LogsQueryOptions, CancellationToken>(
                (_, _, timeRange, _, _) => capturedTimeRange = timeRange)
            .ReturnsAsync(responseMock.Object);

        // Act
        await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, timeRangeInDays);

        // Assert
        Assert.NotNull(capturedTimeRange);
        Assert.Equal(TimeSpan.FromDays(timeRangeInDays), capturedTimeRange);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WhenNoRecords_ReturnsEmptyAndLogsInformation()
    {
        // Arrange
        SetupQueryResults(Array.Empty<QueryResultTransfer>());

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Equal(string.Empty, content);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No transfer data found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesHeaderAndAllFields()
    {
        // Arrange
        var transferId = Guid.NewGuid();
        SetupQueryResults(
        [
            new QueryResultTransfer
            {
                TransferId = transferId,
                CaseId = "C888",
                UserName = "john.doe",
                TransferDirection = "NetApp -> Egress",
                TransferCreated = DateTimeOffset.Parse("2024-01-15T14:30:00Z"),
                TransferCompleted = DateTimeOffset.Parse("2024-01-15T14:35:00Z"),
                TransferredFiles = 24,
                ErrorFiles = 1,
                TotalDataSize = "6.14 GB",
                Status = "Partial"
            }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Contains(
            "TransferId, TransferCreated, TransferCompleted, Status, TransferDirection, UserName, CaseId, TotalDataSize, TransferredFiles, ErrorFiles",
            content);
        Assert.Contains(transferId.ToString(), content);
        Assert.Contains("C888", content);
        Assert.Contains("john.doe", content);
        Assert.Contains("NetApp -> Egress", content);
        Assert.Contains("6.14 GB", content);
        Assert.Contains("Partial", content);
        Assert.Contains("24", content);
    }

    [Theory]
    [InlineData("Success")]
    [InlineData("Partial")]
    [InlineData("Failed")]
    public async Task GenerateCsvContentAsync_WritesStatusFromQueryResult(string status)
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultTransfer { TransferId = Guid.NewGuid(), Status = status }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        Assert.Contains(status, content);
    }

    [Fact]
    public async Task GenerateCsvContentAsync_WritesOneLinePerTransfer()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C001" },
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C002" },
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C003" }
        ]);

        // Act
        var content = await _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0);

        // Assert
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains("C001", content);
        Assert.Contains("C002", content);
        Assert.Contains("C003", content);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing transfer")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(3));
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
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Request to Application Insights failed")),
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, 1.0));

        Assert.Equal("Something went wrong", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error")),
                generalException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateCsvContentAsync_WithInvalidWorkspaceId_ThrowsArgumentException(string? workspaceId)
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, workspaceId!, 1.0));

        Assert.Equal("workspaceId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GenerateCsvContentAsync_WithInvalidTimeRange_ThrowsArgumentOutOfRangeException(double timeRangeInDays)
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _provider.GenerateCsvContentAsync(_logsQueryClientMock.Object, WorkspaceId, timeRangeInDays));

        Assert.Equal("timeRangeInDays", exception.ParamName);
    }
}
