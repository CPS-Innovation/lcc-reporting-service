using CPS.ComplexCases.ReportingService.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class ReportingServiceTests
{
    private readonly Mock<ILogger<ReportingService>> _loggerMock;
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private const string ContainerName = "test-container";

    public ReportingServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReportingService>>();
        _telemetryServiceMock = new Mock<ITelemetryService>();
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Act
        var service = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReportingService(
                null!,
                _telemetryServiceMock.Object,
                _blobStorageServiceMock.Object,
                ContainerName));
    }

    [Fact]
    public void Constructor_WithNullTelemetryService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReportingService(
                _loggerMock.Object,
                null!,
                _blobStorageServiceMock.Object,
                ContainerName));
    }

    [Fact]
    public void Constructor_WithNullBlobStorageService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReportingService(
                _loggerMock.Object,
                _telemetryServiceMock.Object,
                null!,
                ContainerName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidContainerName_ShouldThrowArgumentException(string? invalidContainerName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ReportingService(
                _loggerMock.Object,
                _telemetryServiceMock.Object,
                _blobStorageServiceMock.Object,
                invalidContainerName!));

        Assert.Contains("containerName", exception.Message);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldReturnEarlyWhenNoTransfersFound()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(new List<QueryResultTransfer>());

        // Act
        await reportingService.ProcessReportAsync();

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
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

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
                TotalMegaBytesTransferred = 252.5,
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
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("TransferId, CaseId, Username", capturedContent);
        Assert.Contains("TotalMegaBytesTransferred", capturedContent);
        Assert.Contains(transferId.ToString(), capturedContent);
        Assert.Contains("C456", capturedContent);
        Assert.Contains("testuser", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldGenerateCorrectFileName()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

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
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedFileName);
        Assert.Matches(@"\d{4}-\d{2}/LCC_Transfer_Report_\d{4}-\d{2}-\d{2}\.csv", capturedFileName);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldUploadToCorrectContainer()
    {
        // Arrange
        var customContainerName = "production-reports";
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            customContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = Guid.NewGuid() }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ReturnsAsync(transfers);

        // Act
        await reportingService.ProcessReportAsync();

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(
                customContainerName,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldLogErrorAndRethrowOnException()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var expectedException = new Exception("Test exception");

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync())
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            async () => await reportingService.ProcessReportAsync());

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
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

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
                TotalMegaBytesTransferred = 6287.5,
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
        await reportingService.ProcessReportAsync();

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
        Assert.Contains("6287.5", capturedContent);
        Assert.Contains("125.75", capturedContent);
        Assert.Contains("Partial", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenAllFilesTransferred_ShouldOutputSuccessStatus()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                TotalFiles = 10,
                TransferredFiles = 10,
                ErrorFiles = 0
            }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync()).ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("Success", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenNoFilesTransferred_ShouldOutputFailedStatus()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                TotalFiles = 10,
                TransferredFiles = 0,
                ErrorFiles = 10
            }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync()).ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("Failed", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenSomeFilesTransferredAndSomeFailed_ShouldOutputPartialStatus()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer
            {
                TransferId = Guid.NewGuid(),
                TotalFiles = 10,
                TransferredFiles = 7,
                ErrorFiles = 3
            }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync()).ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("Partial", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldIncludeTransferStatusInHeader()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = Guid.NewGuid() }
        };

        _telemetryServiceMock.Setup(x => x.QueryTransfersAsync()).ReturnsAsync(transfers);

        string? capturedContent = null;
        _blobStorageServiceMock.Setup(x => x.UploadBlobContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((c, f, content) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("TransferStatus", capturedContent);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldProcessMultipleTransfers()
    {
        // Arrange
        var reportingService = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            ContainerName);

        var transfers = new List<QueryResultTransfer>
        {
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C001" },
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C002" },
            new QueryResultTransfer { TransferId = Guid.NewGuid(), CaseId = "C003" }
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
        await reportingService.ProcessReportAsync();

        // Assert
        Assert.NotNull(capturedContent);
        Assert.Contains("C001", capturedContent);
        Assert.Contains("C002", capturedContent);
        Assert.Contains("C003", capturedContent);

        // Verify each transfer was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing transfer")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(3));
    }

    [Theory]
    [InlineData("dev-reports")]
    [InlineData("test-reports")]
    [InlineData("production-reports")]
    public void Constructor_WithDifferentContainerNames_ShouldAcceptValidValues(string containerName)
    {
        // Act
        var service = new ReportingService(
            _loggerMock.Object,
            _telemetryServiceMock.Object,
            _blobStorageServiceMock.Object,
            containerName);

        // Assert
        Assert.NotNull(service);
    }
}