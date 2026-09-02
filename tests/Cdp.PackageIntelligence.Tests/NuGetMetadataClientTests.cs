using Cdp.PackageIntelligence;

namespace Cdp.PackageIntelligence.Tests;

public sealed class NuGetMetadataClientTests
{
    [Fact]
    public async Task GetLatest_returns_stable_for_newtonsoft()
    {
        var client = new NuGetMetadataClient();
        var result = await client.GetLatestAsync("Newtonsoft.Json");
        Assert.False(string.IsNullOrWhiteSpace(result.LatestStableVersion));
        Assert.Equal("Newtonsoft.Json", result.PackageId);
    }
}
