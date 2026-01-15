using Azure.Storage.Blobs;

namespace CPS.ComplexCases.ReportingService.Services;

public class BlobStorageService(
    ILogger<BlobStorageService> logger,
    BlobServiceClient blobServiceClient
) : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient = blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger = logger;

    public async Task UploadBlobContentAsync(
        string containerName,
        string blobName,
        string content)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(blobName);

            await blobClient.UploadAsync(
                BinaryData.FromString(content),
                overwrite: true);

            _logger.LogInformation(
                "{BlobName} successfully uploaded to {ContainerName}",
                blobName,
                containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error uploading blob {BlobName} to {ContainerName}",
                blobName,
                containerName);
            throw;
        }
    }
}
