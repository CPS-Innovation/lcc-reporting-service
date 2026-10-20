using Azure.Core;
using Moq;

namespace CPS.ComplexCases.ReportingService.Services.Tests;

public class BlobServiceClientFactoryTests
{
    private readonly BlobServiceClientFactory _factory = new(Mock.Of<TokenCredential>());

    [Fact]
    public void Constructor_WithNullCredential_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BlobServiceClientFactory(null!));
    }

    [Fact]
    public void GetClient_ReturnsClientForRequestedAccount()
    {
        var client = _factory.GetClient(new Uri("https://racstorage.blob.core.windows.net"));

        Assert.Equal("racstorage", client.AccountName);
    }

    [Fact]
    public void GetClient_ForSameUri_ReturnsCachedClient()
    {
        var first = _factory.GetClient(new Uri("https://racstorage.blob.core.windows.net"));
        var second = _factory.GetClient(new Uri("https://racstorage.blob.core.windows.net"));

        Assert.Same(first, second);
    }

    [Fact]
    public void GetClient_ForDifferentUris_ReturnsDistinctClients()
    {
        var rac = _factory.GetClient(new Uri("https://racstorage.blob.core.windows.net"));
        var lcc = _factory.GetClient(new Uri("https://lccstorage.blob.core.windows.net"));

        Assert.NotSame(rac, lcc);
        Assert.Equal("lccstorage", lcc.AccountName);
    }

    [Fact]
    public void GetClient_WithNullUri_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _factory.GetClient(null!));
    }
}
