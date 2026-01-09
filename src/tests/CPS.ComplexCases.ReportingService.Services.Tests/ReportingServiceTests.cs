using Castle.Core.Logging;
using CPS.ComplexCases.ReportingService.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class ReportingServiceTests
{
    private readonly Mock<ILogger<ReportingService>> _loggerMock;
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly ReportingService _reportingService;

    public ReportingServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReportingService>>();
        _telemetryServiceMock = new Mock<ITelemetryService>();
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldThrowIfBlobContainerNameIsMissing()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", string.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(_reportingService.ProcessReportAsync);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldThrowIfBlobContainerNameIsNull()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(_reportingService.ProcessReportAsync);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldReturnEarlyWhenNoTransfersFound()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", "test-container");
        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(new List<QueryResultTransfer>());

        // Act
        await _reportingService.ProcessReportAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No transfer data found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldUploadFileWithHeaderAndData()
    {
        // Arrange
        var containerName = "test-container";
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", containerName);

        var transferId = Guid.NewGuid();
        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = transferId,
                CaseId = "C456",
                Username = "testuser",
                TransferDirection = "Upload",
                InitiatedTime = DateTimeOffset.Parse("2024-01-01T10:00:00Z"),
                CompletedTime = DateTimeOffset.Parse("2024-01-01T10:05:00Z"),
                DurationFormatted = "00:05:00",
                TotalFiles = 10,
                TransferredFiles = 9,
                ErrorFiles = 1,
                TransferSpeedMbps = 50.5
            }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await _reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("TransferId, CaseId, Username", capturedContent);
        Assert.Contains(transferId.ToString(), capturedContent);
        Assert.Contains("C456", capturedContent);
        Assert.Contains("testuser", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldGenerateCorrectFileName()
    {
        // Arrange
        var containerName = "test-container";
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", containerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = Guid.NewGuid() }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(transfers);

        string? capturedFileName = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string>((c, fileName, content) => capturedFileName = fileName)
            .Returns(Task.CompletedTask);

        // Act
        await _reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedFileName);
        Assert.Matches(@"\d{4}-\d{2}/LCC_Transfer_Report_\d{4}-\d{2}-\d{2}\.csv", capturedFileName);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldUploadToCorrectContainer()
    {
        // Arrange
        var containerName = "production-reports";
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", containerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = Guid.NewGuid() }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(transfers);

        // Act
        await _reportingService.ProcessReportAsync();

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(
                containerName,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }


    [Fact]
    public async Task ProcessReportAsync_ShouldLogErrorAndRethrowOnException()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", "test-container");
        var expectedException = new Exception("Test exception");

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            async () => await _reportingService.ProcessReportAsync());

        Assert.Equal(expectedException, exception);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("An error occurred while processing the report")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldIncludeAllFieldsInOutput()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BlobContainerNameReporting", "test-container");

        var transferId = Guid.NewGuid();
        var initiatedTime = DateTimeOffset.Parse("2024-01-15T14:30:00Z");
        var completedTime = DateTimeOffset.Parse("2024-01-15T14:35:00Z");

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = transferId,
                CaseId = "C888",
                Username = "john.doe",
                TransferDirection = "Download",
                InitiatedTime = initiatedTime,
                CompletedTime = completedTime,
                DurationFormatted = "00:05:00",
                TotalFiles = 25,
                TransferredFiles = 24,
                ErrorFiles = 1,
                TransferSpeedMbps = 125.75
            }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await _reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains(transferId.ToString(), capturedContent);
        Assert.Contains("C888", capturedContent);
        Assert.Contains("john.doe", capturedContent);
        Assert.Contains("Download", capturedContent);
        Assert.Contains("00:05:00", capturedContent);
        Assert.Contains("25", capturedContent);
        Assert.Contains("24", capturedContent);
        Assert.Contains("1", capturedContent);
        Assert.Contains("125.75", capturedContent);
    }
}