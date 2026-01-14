namespace CPS.ComplexCases.ReportingService.Services;

public interface IBlobStorageService
{
    Task UploadBlobContentAsync(string containerName, string blobName, string content);
}
