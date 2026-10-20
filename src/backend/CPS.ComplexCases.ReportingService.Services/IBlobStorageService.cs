namespace CPS.ComplexCases.ReportingService.Services;

public interface IBlobStorageService
{
    Task UploadBlobContentAsync(Uri storageServiceUri, string containerName, string blobName, string content);
}
