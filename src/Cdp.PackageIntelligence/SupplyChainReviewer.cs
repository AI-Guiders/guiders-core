using NuGet.Configuration;

namespace Cdp.PackageIntelligence;

/// <summary>Review NuGet.Config, CPM, and basic supply-chain hygiene for a repo root.</summary>
public sealed class SupplyChainReviewer
{
    public SupplyChainReviewResult Review(string rootDirectory)
    {
        rootDirectory = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException(rootDirectory);

        var observations = new List<string>();
        var dpp = FindUp(rootDirectory, "Directory.Packages.props");
        var cpm = dpp is not null;
        if (cpm)
            observations.Add("central_package_management: Directory.Packages.props present");
        else
            observations.Add("central_package_management: not detected");

        var nugetConfig = FindNuGetConfig(rootDirectory);
        IReadOnlyList<PackageSourceInfo> sources;
        if (nugetConfig is not null)
        {
            observations.Add($"nuget_config: {nugetConfig}");
            sources = ReadSources(nugetConfig);
        }
        else
        {
            observations.Add("nuget_config: none found walking up — SDK defaults apply");
            sources = Array.Empty<PackageSourceInfo>();
        }

        if (sources.Any(s => s.Source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase)))
            observations.Add("feed: nuget.org configured");
        else if (sources.Count > 0)
            observations.Add("feed: no nuget.org in discovered sources — verify private feed trust");

        var lockFiles = Directory.EnumerateFiles(rootDirectory, "packages.lock.json", SearchOption.AllDirectories)
            .Take(5)
            .ToArray();
        if (lockFiles.Length > 0)
            observations.Add($"lock_files: {lockFiles.Length} packages.lock.json (capped scan)");

        return new SupplyChainReviewResult(rootDirectory, cpm, dpp, sources, observations);
    }

    static IReadOnlyList<PackageSourceInfo> ReadSources(string configPath)
    {
        var settings = Settings.LoadDefaultSettings(Path.GetDirectoryName(configPath)!);
        var provider = new PackageSourceProvider(settings);
        return provider.LoadPackageSources()
            .Select(s => new PackageSourceInfo(s.Name, s.Source, s.IsEnabled))
            .ToArray();
    }

    static string? FindNuGetConfig(string start)
    {
        var dir = start;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            foreach (var name in new[] { "NuGet.Config", "nuget.config" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }

        return null;
    }

    static string? FindUp(string startDir, string fileName)
    {
        var dir = startDir;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }

        return null;
    }
}
