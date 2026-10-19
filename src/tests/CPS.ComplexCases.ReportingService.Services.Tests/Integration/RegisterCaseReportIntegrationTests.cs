using Azure;
using Azure.Monitor.Query;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CPS.ComplexCases.ReportingService.Domain.Configuration;
using CPS.ComplexCases.ReportingService.Domain.Models;
using CPS.ComplexCases.ReportingService.Services.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests.Integration;

/// <summary>
/// Exercises the Register a Case report end to end through the real DI graph, configuration
/// binding, provider, reporting service and blob storage service. Only the Azure SDK clients are mocked.
/// </summary>
public class RegisterCaseReportIntegrationTests
{
    private const string WorkspaceId = "rac-workspace-id";
    private const string RacStorageServiceUri = "https://racstorage.blob.core.windows.net";
    private const string Container = "reports";
    private const string StoragePath = "register-case/journeys";

    private readonly Mock<LogsQueryClient> _logsQueryClientMock = new();
    private readonly Mock<BlobServiceClient> _racBlobServiceClientMock = new();
    private readonly Mock<IBlobServiceClientFactory> _blobServiceClientFactoryMock = new();
    private readonly Mock<BlobContainerClient> _containerClientMock = new();
    private readonly Mock<BlobClient> _blobClientMock = new();

    private string? _uploadedBlobName;
    private string? _uploadedContent;

    public RegisterCaseReportIntegrationTests()
    {
        _blobServiceClientFactoryMock
            .Setup(x => x.GetClient(new Uri(RacStorageServiceUri)))
            .Returns(_racBlobServiceClientMock.Object);

        _racBlobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(Container))
            .Returns(_containerClientMock.Object);

        _containerClientMock
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        _containerClientMock
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => _uploadedBlobName = name)
            .Returns(_blobClientMock.Object);

        _blobClientMock
            .Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
            .Callback<BinaryData, bool, CancellationToken>((data, _, _) => _uploadedContent = data.ToString())
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
    }

    private IReportingService BuildReportingService(bool enabled = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reports:RegisterCase:Enabled"] = enabled.ToString(),
                ["Reports:RegisterCase:CronSchedule"] = "0 0 1 * * *",
                ["Reports:RegisterCase:WorkspaceId"] = WorkspaceId,
                ["Reports:RegisterCase:TimeRangeDays"] = "1.0",
                ["Reports:RegisterCase:StorageServiceUri"] = RacStorageServiceUri,
                ["Reports:RegisterCase:StorageContainer"] = Container,
                ["Reports:RegisterCase:StoragePath"] = StoragePath,
                ["Reports:TransferMaterial:Enabled"] = "true",
                ["Reports:TransferMaterial:WorkspaceId"] = "lcc-workspace-id",
                ["Reports:TransferMaterial:StorageServiceUri"] = "https://lccstorage.blob.core.windows.net",
                ["Reports:TransferMaterial:StorageContainer"] = "lcc-reports",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ReportsOptions>(configuration);
        services.AddSingleton(_blobServiceClientFactoryMock.Object);
        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddSingleton(_logsQueryClientMock.Object);
        services.AddSingleton<IReportProvider, TransferMaterialReportProvider>();
        services.AddSingleton<IReportProvider, RegisterCaseReportProvider>();
        services.AddSingleton<IReportingService, ReportingService>();

        return services.BuildServiceProvider().GetRequiredService<IReportingService>();
    }

    private void SetupQueryResults(IReadOnlyList<QueryResultRegisterCase> results)
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
            .ReturnsAsync(responseMock.Object);
    }

    [Fact]
    public async Task ProcessReportAsync_UploadsCsvToRegisterCaseStorageAccountAtDatedPath()
    {
        // Arrange
        SetupQueryResults(
        [
            new QueryResultRegisterCase
            {
                JourneyId = "journey-1",
                Username = "jane.doe@cps.gov.uk",
                AreaOrDivision = "Humberside",
                StartedAt = DateTimeOffset.Parse("2026-10-14T09:00:00Z"),
                CompletedAt = DateTimeOffset.Parse("2026-10-14T09:30:00Z"),
                DropOffScreen = "",
                Urn = "45AA1234526",
                Outcome = "Completed"
            },
            new QueryResultRegisterCase
            {
                JourneyId = "journey-2",
                Username = "john.smith@cps.gov.uk",
                StartedAt = DateTimeOffset.Parse("2026-10-14T08:00:00Z"),
                DropOffScreen = "Case details",
                Outcome = "Abandoned"
            }
        ]);

        var reportingService = BuildReportingService();

        // Act
        await reportingService.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName);

        // Assert - routed to the Register a Case storage account
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(new Uri(RacStorageServiceUri)), Times.Once);
        _racBlobServiceClientMock.Verify(x => x.GetBlobContainerClient(Container), Times.Once);

        // Assert - path
        var now = DateTime.UtcNow;
        Assert.Equal($"{StoragePath}/{now:yyyy-MM}/RAC_Journey_Report_{now:yyyy-MM-dd}.csv", _uploadedBlobName);

        // Assert - content
        Assert.NotNull(_uploadedContent);
        var lines = _uploadedContent.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "JourneyId,Username,AreaOrDivision,StartedAt,CompletedAt,DropOffScreen,Urn,Outcome",
            "journey-1,jane.doe@cps.gov.uk,Humberside,2026-10-14T09:00:00.000Z,2026-10-14T09:30:00.000Z,,45AA1234526,Completed",
            "journey-2,john.smith@cps.gov.uk,,2026-10-14T08:00:00.000Z,,Case details,,Abandoned"
        ], lines);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenQueryReturnsNoRows_DoesNotCreateBlob()
    {
        // Arrange
        SetupQueryResults([]);
        var reportingService = BuildReportingService();

        // Act
        await reportingService.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName);

        // Assert
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(It.IsAny<Uri>()), Times.Never);
        _containerClientMock.Verify(
            x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _blobClientMock.Verify(
            x => x.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessReportAsync_WhenDisabled_DoesNotQueryOrUpload()
    {
        // Arrange
        var reportingService = BuildReportingService(enabled: false);

        // Act
        await reportingService.ProcessReportAsync(RegisterCaseReportProvider.ReportKeyName);

        // Assert
        _logsQueryClientMock.VerifyNoOtherCalls();
        _blobServiceClientFactoryMock.VerifyNoOtherCalls();
    }
}
