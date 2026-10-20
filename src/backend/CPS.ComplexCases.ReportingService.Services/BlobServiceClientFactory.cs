using System.Collections.Concurrent;
using Azure.Core;
using Azure.Storage.Blobs;

namespace CPS.ComplexCases.ReportingService.Services;

public class BlobServiceClientFactory(TokenCredential credential) : IBlobServiceClientFactory
{
    private readonly TokenCredential _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    private readonly ConcurrentDictionary<Uri, BlobServiceClient> _clients = new();

    public BlobServiceClient GetClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);

        return _clients.GetOrAdd(serviceUri, uri => new BlobServiceClient(uri, _credential));
    }
}
