using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class BlobStorageServiceTests
{
    private readonly Mock<ILogger<BlobStorageService>> _loggerMock;
    private readonly Mock<BlobServiceClient> _blobServiceClientMock;
    private readonly Mock<IBlobServiceClientFactory> _blobServiceClientFactoryMock;
    private readonly BlobStorageService _blobStorageService;
    private static readonly Uri StorageServiceUri = new("https://teststorage.blob.core.windows.net");

    public BlobStorageServiceTests()
    {
        _loggerMock = new Mock<ILogger<BlobStorageService>>();
        _blobServiceClientMock = new Mock<BlobServiceClient>();
        _blobServiceClientFactoryMock = new Mock<IBlobServiceClientFactory>();
        _blobServiceClientFactoryMock
            .Setup(x => x.GetClient(StorageServiceUri))
            .Returns(_blobServiceClientMock.Object);
        _blobStorageService = new BlobStorageService(
            _loggerMock.Object,
            _blobServiceClientFactoryMock.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BlobStorageService(null!, _blobServiceClientFactoryMock.Object));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullBlobServiceClientFactory_ShouldThrowArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BlobStorageService(_loggerMock.Object, null!));

        Assert.Equal("blobServiceClientFactory", exception.ParamName);
    }

    [Fact]
    public async Task UploadBlobContentAsync_WithNullStorageServiceUri_ShouldThrowArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _blobStorageService.UploadBlobContentAsync(null!, "container", "blob", "content"));

        Assert.Equal("storageServiceUri", exception.ParamName);
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(It.IsAny<Uri>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadBlobContentAsync_WithInvalidContainerName_ShouldThrowArgumentException(string? containerName)
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _blobStorageService.UploadBlobContentAsync(StorageServiceUri, containerName!, "blob", "content"));

        Assert.Equal("containerName", exception.ParamName);
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(It.IsAny<Uri>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadBlobContentAsync_WithInvalidBlobName_ShouldThrowArgumentException(string? blobName)
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _blobStorageService.UploadBlobContentAsync(StorageServiceUri, "container", blobName!, "content"));

        Assert.Equal("blobName", exception.ParamName);
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task UploadBlobContentAsync_WithNullContent_ShouldThrowArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _blobStorageService.UploadBlobContentAsync(StorageServiceUri, "container", "blob", null!));

        Assert.Equal("content", exception.ParamName);
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task UploadBlobContentAsync_ShouldUploadContentToBlobStorage()
    {
        // Arrange
        const string containerName = "test-container";
        const string blobName = "test-blob.json";
        const string content = "test content";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        _blobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<BinaryData>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _blobStorageService.UploadBlobContentAsync(StorageServiceUri, containerName, blobName, content);

        // Assert
        _blobServiceClientMock.Verify(
            x => x.GetBlobContainerClient(containerName),
            Times.Once);

        mockContainerClient.Verify(
            x => x.GetBlobClient(blobName),
            Times.Once);

        mockBlobClient.Verify(
            x => x.UploadAsync(
                It.IsAny<BinaryData>(),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains(blobName) &&
                    v.ToString()!.Contains(containerName)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadBlobContentAsync_ShouldUseClientForRequestedStorageAccount()
    {
        // Arrange
        const string containerName = "reports";
        const string blobName = "register-case/journeys/2026-10/RAC_Journey_Report_2026-10-15.csv";
        var storageServiceUri = new Uri("https://racstorage.blob.core.windows.net");

        var targetServiceClientMock = new Mock<BlobServiceClient>();
        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        _blobServiceClientFactoryMock
            .Setup(x => x.GetClient(storageServiceUri))
            .Returns(targetServiceClientMock.Object);

        targetServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _blobStorageService.UploadBlobContentAsync(storageServiceUri, containerName, blobName, "content");

        // Assert
        _blobServiceClientFactoryMock.Verify(x => x.GetClient(storageServiceUri), Times.Once);
        mockBlobClient.Verify(
            x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
        _blobServiceClientMock.Verify(x => x.GetBlobContainerClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadBlobContentAsync_ShouldUploadCorrectContent()
    {
        // Arrange
        const string containerName = "test-container";
        const string blobName = "test-blob.json";
        const string content = "expected content";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        BinaryData? capturedBinaryData = null;

        _blobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<BinaryData>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<BinaryData, bool, CancellationToken>((data, _, _) =>
            {
                capturedBinaryData = data;
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _blobStorageService.UploadBlobContentAsync(StorageServiceUri, containerName, blobName, content);

        // Assert
        Assert.NotNull(capturedBinaryData);
        Assert.Equal(content, capturedBinaryData!.ToString());
    }

    [Fact]
    public async Task UploadBlobContentAsync_ShouldHandleExceptionsAndLogError()
    {
        // Arrange
        const string containerName = "test-container";
        const string blobName = "test-blob.json";
        const string content = "test content";

        var expectedException = new RequestFailedException("Storage error");

        _blobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Throws(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _blobStorageService.UploadBlobContentAsync(StorageServiceUri, containerName, blobName, content));

        Assert.Equal("Storage error", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains(blobName) &&
                    v.ToString()!.Contains(containerName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadBlobContentAsync_ShouldHandleUploadFailure()
    {
        // Arrange
        const string containerName = "test-container";
        const string blobName = "test-blob.json";
        const string content = "test content";

        var expectedException = new RequestFailedException("Upload failed");

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        _blobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<BinaryData>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _blobStorageService.UploadBlobContentAsync(StorageServiceUri, containerName, blobName, content));

        Assert.Equal("Upload failed", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains(blobName) &&
                    v.ToString()!.Contains(containerName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
