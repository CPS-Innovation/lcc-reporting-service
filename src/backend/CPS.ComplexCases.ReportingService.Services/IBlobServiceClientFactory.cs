using Azure.Storage.Blobs;

namespace CPS.ComplexCases.ReportingService.Services;

public interface IBlobServiceClientFactory
{
    BlobServiceClient GetClient(Uri serviceUri);
}
