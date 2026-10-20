namespace CPS.ComplexCases.ReportingService.Services;

public class BlobStorageService(
    ILogger<BlobStorageService> logger,
    IBlobServiceClientFactory blobServiceClientFactory
) : IBlobStorageService
{
    private readonly IBlobServiceClientFactory _blobServiceClientFactory =
        blobServiceClientFactory ?? throw new ArgumentNullException(nameof(blobServiceClientFactory));
    private readonly ILogger<BlobStorageService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task UploadBlobContentAsync(
        Uri storageServiceUri,
        string containerName,
        string blobName,
        string content)
    {
        ArgumentNullException.ThrowIfNull(storageServiceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var containerClient = _blobServiceClientFactory
                .GetClient(storageServiceUri)
                .GetBlobContainerClient(containerName);
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
