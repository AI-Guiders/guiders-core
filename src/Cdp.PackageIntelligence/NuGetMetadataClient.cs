using Cdp.PackageIntelligence.Internal;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Cdp.PackageIntelligence;

/// <summary>Resolve package versions from NuGet feeds via NuGet.Protocol.</summary>
public sealed class NuGetMetadataClient
{
    private readonly string _sourceUrl;

    public NuGetMetadataClient(string? sourceUrl = null)
    {
        _sourceUrl = string.IsNullOrWhiteSpace(sourceUrl)
            ? "https://api.nuget.org/v3/index.json"
            : sourceUrl;
    }

    public async Task<LatestVersionResult> GetLatestAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("package id is required", nameof(packageId));

        var repo = Repository.Factory.GetCoreV3(_sourceUrl);
        var resource = await repo.GetResourceAsync<FindPackageByIdResource>(ct).ConfigureAwait(false);
        var versions = (await resource.GetAllVersionsAsync(
            packageId,
            new SourceCacheContext(),
            NullLogger.Instance,
            ct).ConfigureAwait(false)).ToList();

        if (versions.Count == 0)
        {
            return new LatestVersionResult(packageId, null, null, includePrerelease, _sourceUrl);
        }

        var ordered = versions.OrderByDescending(v => v).ToList();
        var latestStable = ordered.FirstOrDefault(v => !v.IsPrerelease)?.ToNormalizedString();
        var latest = includePrerelease
            ? ordered[0].ToNormalizedString()
            : latestStable;

        return new LatestVersionResult(packageId, latest, latestStable, includePrerelease, _sourceUrl);
    }

    public async Task<string?> ResolveUpgradeTargetAsync(
        string packageId,
        string currentVersion,
        bool includePrerelease,
        CancellationToken ct)
    {
        var latest = await GetLatestAsync(packageId, includePrerelease, ct).ConfigureAwait(false);
        var target = latest.LatestVersion;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        if (!NuGetVersion.TryParse(currentVersion, out var current)
            || !NuGetVersion.TryParse(target, out var targetVer))
            return target;

        return targetVer > current ? target : null;
    }
}
