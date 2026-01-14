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
    private readonly BlobStorageService _blobStorageService;

    public BlobStorageServiceTests()
    {
        _loggerMock = new Mock<ILogger<BlobStorageService>>();
        _blobServiceClientMock = new Mock<BlobServiceClient>();
        _blobStorageService = new BlobStorageService(_loggerMock.Object, _blobServiceClientMock.Object);
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
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _blobStorageService.UploadBlobContentAsync(containerName, blobName, content);

        // Assert
        _blobServiceClientMock.Verify(x => x.GetBlobContainerClient(containerName), Times.Once);
        mockContainerClient.Verify(x => x.GetBlobClient(blobName), Times.Once);
        mockBlobClient.Verify(x => x.UploadAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()), Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(blobName) && v.ToString()!.Contains(containerName)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
        Stream? capturedStream = null;

        _blobServiceClientMock
            .Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, bool, CancellationToken>((stream, overwrite, token) =>
            {
                var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                capturedStream = memoryStream;
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _blobStorageService.UploadBlobContentAsync(containerName, blobName, content);

        // Assert
        Assert.NotNull(capturedStream);
        capturedStream.Position = 0;
        using var reader = new StreamReader(capturedStream);
        var uploadedContent = await reader.ReadToEndAsync();
        Assert.Equal(content, uploadedContent);
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
            async () => await _blobStorageService.UploadBlobContentAsync(containerName, blobName, content));

        Assert.Equal("Storage error", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(blobName) && v.ToString()!.Contains(containerName)),
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
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        mockContainerClient
            .Setup(x => x.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            async () => await _blobStorageService.UploadBlobContentAsync(containerName, blobName, content));

        Assert.Equal("Upload failed", exception.Message);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(blobName) && v.ToString()!.Contains(containerName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}