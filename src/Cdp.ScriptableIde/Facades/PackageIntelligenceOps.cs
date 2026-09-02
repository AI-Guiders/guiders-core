using Cdp.PackageIntelligence;

namespace Cdp.ScriptableIde;

/// <summary>L1 package intelligence — audit, latest, upgrade plan, supply chain (csharp/NuGet).</summary>
public static class PackageIntelligenceOps
{
    private static readonly VulnerabilityAuditor Auditor = new();
    private static readonly NuGetMetadataClient Metadata = new();
    private static readonly UpgradePlanBuilder Planner = new();
    private static readonly SupplyChainReviewer SupplyChain = new();

    public static async Task<StepResponse> AuditAsync(
        ScriptToolBus bus,
        PlanContext plan,
        string? projectPath = null,
        bool includeTransitive = true,
        CancellationToken ct = default)
    {
        const string kind = "packages.audit";
        if (!TryResolveCsharpAnchor(plan, projectPath, out var anchor, out var err))
            return FailRecord(bus, kind, err);

        if (bus.IsDryRun)
        {
            var dry = StepResponse.Success(kind, "dry_run", new { dry_run = true, anchor, includeTransitive });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor, includeTransitive }), dry.ToJson(), skippedDryRun: true);
            return dry;
        }

        try
        {
            var result = await Auditor.AuditAsync(anchor, includeTransitive, ct).ConfigureAwait(false);
            var payload = new
            {
                projection = "nuget",
                anchor = result.AnchorPath,
                has_vulnerabilities = result.HasVulnerabilities,
                count = result.Packages.Count,
                sources = result.Sources,
                packages = result.Packages.Select(p => new
                {
                    id = p.PackageId,
                    requested = p.RequestedVersion,
                    resolved = p.ResolvedVersion,
                    framework = p.Framework,
                    project = p.ProjectPath,
                    transitive = p.IsTransitive,
                    advisories = p.Advisories.Select(a => new { severity = a.Severity, url = a.AdvisoryUrl })
                })
            };
            var summary = result.HasVulnerabilities ? $"vulnerable:{result.Packages.Count}" : "clean";
            var step = StepResponse.Success(kind, summary, payload);
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor, includeTransitive }), step.ToJson());
            return step;
        }
        catch (Exception ex)
        {
            return FailRecord(bus, kind, ex.Message, anchor);
        }
    }

    public static async Task<StepResponse> LatestAsync(
        ScriptToolBus bus,
        PlanContext plan,
        string packageId,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        const string kind = "packages.latest";
        if (string.IsNullOrWhiteSpace(packageId))
            return StepResponse.Fail(kind, "id is required");

        if (PackageOps.ResolveLang(plan) != "csharp")
            return FailRecord(bus, kind, "packages.latest is csharp/NuGet only. cdp_open csharp first.");

        if (bus.IsDryRun)
        {
            var dry = StepResponse.Success(kind, "dry_run", new { dry_run = true, packageId, includePrerelease });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { packageId, includePrerelease }), dry.ToJson(), skippedDryRun: true);
            return dry;
        }

        try
        {
            var result = await Metadata.GetLatestAsync(packageId, includePrerelease, ct).ConfigureAwait(false);
            var step = StepResponse.Success(kind, result.LatestVersion ?? "not_found", new
            {
                projection = "nuget",
                id = result.PackageId,
                latest = result.LatestVersion,
                latest_stable = result.LatestStableVersion,
                include_prerelease = result.IncludePrerelease,
                source = result.Source
            });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { packageId, includePrerelease }), step.ToJson());
            return step;
        }
        catch (Exception ex)
        {
            return FailRecord(bus, kind, ex.Message, packageId);
        }
    }

    public static async Task<StepResponse> UpgradePlanAsync(
        ScriptToolBus bus,
        PlanContext plan,
        string? projectPath = null,
        bool includeTransitive = true,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        const string kind = "packages.upgrade_plan";
        if (!TryResolveCsharpAnchor(plan, projectPath, out var anchor, out var err))
            return FailRecord(bus, kind, err);

        if (bus.IsDryRun)
        {
            var dry = StepResponse.Success(kind, "dry_run", new { dry_run = true, anchor, includeTransitive, includePrerelease });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor, includeTransitive, includePrerelease }), dry.ToJson(), skippedDryRun: true);
            return dry;
        }

        try
        {
            var result = await Planner.PlanVulnerabilityFixesAsync(anchor, includeTransitive, includePrerelease, ct)
                .ConfigureAwait(false);
            var step = StepResponse.Success(kind, result.HasVulnerabilities ? $"actions:{result.Actions.Count}" : "clean", new
            {
                projection = "nuget",
                anchor = result.AnchorPath,
                has_vulnerabilities = result.HasVulnerabilities,
                strategy = result.Strategy,
                apply_command = result.ApplyCommand,
                note = result.Note,
                actions = result.Actions.Select(a => new
                {
                    id = a.PackageId,
                    current = a.CurrentVersion,
                    target = a.TargetVersion,
                    severity = a.Severity,
                    project = a.ProjectPath,
                    transitive = a.IsTransitive,
                    rationale = a.Rationale
                })
            });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor, includeTransitive, includePrerelease }), step.ToJson());
            return step;
        }
        catch (Exception ex)
        {
            return FailRecord(bus, kind, ex.Message, anchor);
        }
    }

    public static async Task<StepResponse> FixVulnAsync(
        ScriptToolBus bus,
        PlanContext plan,
        string? projectPath = null,
        CancellationToken ct = default)
    {
        const string kind = "packages.fix_vuln";
        if (!TryResolveCsharpAnchor(plan, projectPath, out var anchor, out var err))
            return FailRecord(bus, kind, err);

        if (bus.IsDryRun)
        {
            var cmd = SdkVulnerabilityUpdater.FormatApplyCommand(anchor);
            var dry = StepResponse.Success(kind, "dry_run", new { dry_run = true, anchor, apply_command = cmd });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor }), dry.ToJson(), skippedDryRun: true);
            return dry;
        }

        try
        {
            var result = await Planner.ApplyVulnerabilityFixesAsync(anchor, ct).ConfigureAwait(false);
            var payload = new
            {
                projection = "nuget",
                anchor = result.AnchorPath,
                strategy = "sdk_dotnet_package_update_vulnerable",
                apply_command = SdkVulnerabilityUpdater.FormatApplyCommand(anchor),
                exit_code = result.ExitCode,
                stdout = Trunc(result.StdOut, 6000),
                stderr = Trunc(result.StdErr, 2000)
            };
            var step = result.Ok
                ? StepResponse.Success(kind, "applied", payload)
                : StepResponse.Fail(kind, $"dotnet exit {result.ExitCode}", payload);
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { anchor }), step.ToJson());
            return step;
        }
        catch (Exception ex)
        {
            return FailRecord(bus, kind, ex.Message, anchor);
        }
    }

    public static Task<StepResponse> SupplyChainAsync(
        ScriptToolBus bus,
        PlanContext plan,
        string? root = null,
        CancellationToken ct = default)
    {
        const string kind = "packages.supply_chain";
        _ = ct;
        var lang = PackageOps.ResolveLang(plan);
        if (lang != "csharp")
            return Task.FromResult(FailRecord(bus, kind, "packages.supply_chain is csharp/NuGet only. cdp_open csharp first."));

        var dir = !string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(root)
            : !string.IsNullOrWhiteSpace(plan.SolutionOrProjectPath)
                ? Path.GetDirectoryName(Path.GetFullPath(plan.SolutionOrProjectPath))!
                : plan.WorkRoot;

        if (bus.IsDryRun)
        {
            var dry = StepResponse.Success(kind, "dry_run", new { dry_run = true, root = dir });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { root = dir }), dry.ToJson(), skippedDryRun: true);
            return Task.FromResult(dry);
        }

        try
        {
            var result = SupplyChain.Review(dir);
            var step = StepResponse.Success(kind, $"sources:{result.Sources.Count}", new
            {
                projection = "nuget",
                root = result.RootDirectory,
                central_package_management = result.CentralPackageManagement,
                directory_packages_props = result.DirectoryPackagesPropsPath,
                sources = result.Sources.Select(s => new { name = s.Name, source = s.Source, enabled = s.IsEnabled }),
                observations = result.Observations
            });
            bus.RecordLocal("packages", kind, ScriptArgs.From(new { root = dir }), step.ToJson());
            return Task.FromResult(step);
        }
        catch (Exception ex)
        {
            return Task.FromResult(FailRecord(bus, kind, ex.Message, dir));
        }
    }

    public static bool TryResolveCsharpAnchor(PlanContext plan, string? pathOverride, out string anchor, out string error)
    {
        anchor = "";
        error = "";
        if (PackageOps.ResolveLang(plan) != "csharp")
        {
            error = "Package intelligence requires csharp session. cdp_open a .csproj or .sln.";
            return false;
        }

        plan = string.IsNullOrWhiteSpace(pathOverride)
            ? plan
            : new PlanContext
            {
                PrimaryRoot = plan.PrimaryRoot,
                WorkRoot = plan.WorkRoot,
                PlanId = plan.PlanId,
                SolutionOrProjectPath = Path.GetFullPath(pathOverride),
                Language = plan.Language,
                Settings = plan.Settings
            };

        var path = plan.SolutionOrProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No csharp project/solution in session. cdp_open a .csproj or .sln (or pass path=).";
            return false;
        }

        path = Path.GetFullPath(path);
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                error = $"Anchor not found: {path}";
                return false;
            }

            anchor = path;
            return true;
        }

        error = $"Unsupported anchor for package intelligence: {path}";
        return false;
    }

    private static string? Trunc(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static StepResponse FailRecord(ScriptToolBus bus, string kind, string error, object? extra = null)
    {
        var fail = StepResponse.Fail(kind, error, extra);
        bus.RecordLocal("packages", kind, ScriptArgs.From(extra ?? new { }), fail.ToJson());
        return fail;
    }
}
