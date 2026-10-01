using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Configuration;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class ReportingServiceTests
{
    private readonly Mock<ILogger<ReportingService>> _loggerMock;
    private readonly Mock<IReportProvider> _reportProviderMock;
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly LogsQueryClient _logsQueryClient;
    private const string ReportKey = "TransferMaterial";
    private const string ContainerName = "test-container";
    private const string StoragePath = "case-materials/transfers";
    private const string FileName = "2024-01/LCC_Transfer_Report_2024-01-15.csv";

    public ReportingServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReportingService>>();
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _logsQueryClient = new Mock<LogsQueryClient>().Object;

        _reportProviderMock = new Mock<IReportProvider>();
        _reportProviderMock.SetupGet(x => x.ReportKey).Returns(ReportKey);
        _reportProviderMock.Setup(x => x.GenerateFileName()).Returns(FileName);
    }

    private static ReportConfig CreateConfig(
        bool enabled = true,
        string workspaceId = "test-workspace-id",
        string storageContainer = ContainerName,
        string storagePath = StoragePath,
        double timeRangeDays = 1.0) => new()
        {
            Enabled = enabled,
            WorkspaceId = workspaceId,
            StorageContainer = storageContainer,
            StoragePath = storagePath,
            TimeRangeDays = timeRangeDays
        };

    private ReportingService CreateService(ReportConfig? config, string configKey = ReportKey)
    {
        var options = new ReportsOptions();
        if (config is not null)
        {
            options.Reports[configKey] = config;
        }

        return new ReportingService(
            _loggerMock.Object,
            [_reportProviderMock.Object],
            _blobStorageServiceMock.Object,
            _logsQueryClient,
            Options.Create(options));
    }

    private void SetupProviderContent(string content) =>
        _reportProviderMock
            .Setup(x => x.GenerateCsvContentAsync(It.IsAny<LogsQueryClient>(), It.IsAny<string>(), It.IsAny<double>()))
            .ReturnsAsync(content);

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ReportingService(
                null!,
                [_reportProviderMock.Object],
                _blobStorageServiceMock.Object,
                _logsQueryClient,
                Options.Create(new ReportsOptions())));
    }

    [Fact]
    public void Constructor_WithNullBlobStorageService_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ReportingService(
                _loggerMock.Object,
                [_reportProviderMock.Object],
                null!,
                _logsQueryClient,
                Options.Create(new ReportsOptions())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessReportAsync_WithInvalidReportKey_ShouldThrowArgumentException(string? reportKey)
    {
        // Arrange
        var reportingService = CreateService(CreateConfig());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => reportingService.ProcessReportAsync(reportKey!));

        Assert.Equal("reportKey", exception.ParamName);
    }

    [Fact]
    public async Task ProcessReportAsync_WithUnknownReportKey_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(), configKey: "RegisterACase");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reportingService.ProcessReportAsync("RegisterACase"));

        Assert.Contains("RegisterACase", exception.Message);

        _blobStorageServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessReportAsync_WithNoConfigurationForReport_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var reportingService = CreateService(config: null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reportingService.ProcessReportAsync(ReportKey));

        Assert.Contains($"Reports:{ReportKey}", exception.Message);
    }

    [Fact]
    public async Task ProcessReportAsync_WithConfigurationKeyInDifferentCase_ShouldStillResolveConfig()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(), configKey: "transfermaterial");
        SetupProviderContent("header\r\nrow");

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(ContainerName, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenReportDisabled_ShouldSkipProviderAndUpload()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(enabled: false));

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("is disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _reportProviderMock.Verify(
            x => x.GenerateCsvContentAsync(It.IsAny<LogsQueryClient>(), It.IsAny<string>(), It.IsAny<double>()),
            Times.Never);

        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessReportAsync_WithMissingWorkspaceId_ShouldThrowInvalidOperationException(string workspaceId)
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(workspaceId: workspaceId));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reportingService.ProcessReportAsync(ReportKey));

        Assert.Contains("WorkspaceId", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessReportAsync_WithMissingStorageContainer_ShouldThrowInvalidOperationException(string storageContainer)
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(storageContainer: storageContainer));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reportingService.ProcessReportAsync(ReportKey));

        Assert.Contains("StorageContainer", exception.Message);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldPassConfiguredWorkspaceAndTimeRangeToProvider()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(workspaceId: "lcc-workspace", timeRangeDays: 7.5));
        SetupProviderContent("header\r\nrow");

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _reportProviderMock.Verify(
            x => x.GenerateCsvContentAsync(_logsQueryClient, "lcc-workspace", 7.5),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenProviderReturnsNoContent_ShouldNotUpload()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig());
        SetupProviderContent(string.Empty);

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldUploadContentToConfiguredContainerAndPath()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig());
        var content = "header\r\nrow";
        SetupProviderContent(content);

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(
                ContainerName,
                $"{StoragePath}/{FileName}",
                content),
            Times.Once);
    }

    [Theory]
    [InlineData("case-materials/transfers/")]
    [InlineData("/case-materials/transfers")]
    [InlineData(" case-materials/transfers ")]
    public async Task ProcessReportAsync_ShouldNormaliseStoragePathSeparators(string storagePath)
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(storagePath: storagePath));
        SetupProviderContent("header\r\nrow");

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(
                ContainerName,
                $"case-materials/transfers/{FileName}",
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_WithEmptyStoragePath_ShouldUploadToContainerRoot()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig(storagePath: string.Empty));
        SetupProviderContent("header\r\nrow");

        // Act
        await reportingService.ProcessReportAsync(ReportKey);

        // Assert
        _blobStorageServiceMock.Verify(
            x => x.UploadBlobContentAsync(ContainerName, FileName, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessReportAsync_ShouldLogErrorAndRethrowOnException()
    {
        // Arrange
        var reportingService = CreateService(CreateConfig());
        var expectedException = new Exception("Test exception");

        _reportProviderMock
            .Setup(x => x.GenerateCsvContentAsync(It.IsAny<LogsQueryClient>(), It.IsAny<string>(), It.IsAny<double>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            () => reportingService.ProcessReportAsync(ReportKey));

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
}
