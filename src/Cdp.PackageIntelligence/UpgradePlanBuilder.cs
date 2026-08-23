namespace Cdp.PackageIntelligence;

/// <summary>Build agent-actionable upgrade plans from vulnerability audit; apply via SDK.</summary>
public sealed class UpgradePlanBuilder
{
    private readonly VulnerabilityAuditor _auditor;
    private readonly NuGetMetadataClient _metadata;
    private readonly SdkVulnerabilityUpdater _sdk;

    public UpgradePlanBuilder(
        VulnerabilityAuditor? auditor = null,
        NuGetMetadataClient? metadata = null,
        SdkVulnerabilityUpdater? sdk = null)
    {
        _auditor = auditor ?? new VulnerabilityAuditor();
        _metadata = metadata ?? new NuGetMetadataClient();
        _sdk = sdk ?? new SdkVulnerabilityUpdater();
    }

    public async Task<UpgradePlanResult> PlanVulnerabilityFixesAsync(
        string anchorPath,
        bool includeTransitive = true,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        var audit = await _auditor.AuditAsync(anchorPath, includeTransitive, ct).ConfigureAwait(false);
        if (!audit.HasVulnerabilities)
        {
            return new UpgradePlanResult(
                audit.AnchorPath,
                false,
                Array.Empty<UpgradeAction>(),
                "no_vulnerabilities",
                Strategy: SdkStrategy,
                ApplyCommand: SdkVulnerabilityUpdater.FormatApplyCommand(audit.AnchorPath));
        }

        var actions = new List<UpgradeAction>();
        foreach (var pkg in audit.Packages)
        {
            var severity = pkg.Advisories
                .Select(a => a.Severity)
                .OrderByDescending(SeverityRank)
                .FirstOrDefault() ?? "Unknown";

            // Optional feed hint for agents (SDK resolves graph on apply).
            var targetHint = await _metadata.ResolveUpgradeTargetAsync(
                pkg.PackageId,
                pkg.ResolvedVersion,
                includePrerelease,
                ct).ConfigureAwait(false);

            actions.Add(new UpgradeAction(
                pkg.PackageId,
                pkg.ResolvedVersion,
                targetHint,
                severity,
                pkg.ProjectPath,
                pkg.IsTransitive,
                targetHint is null ? "sdk_resolve_on_apply" : "sdk_resolve_on_apply; feed_hint"));
        }

        return new UpgradePlanResult(
            audit.AnchorPath,
            true,
            actions,
            "plan_only; apply with cdp_pkg_fix_vuln (dotnet package update --vulnerable)",
            Strategy: SdkStrategy,
            ApplyCommand: SdkVulnerabilityUpdater.FormatApplyCommand(audit.AnchorPath));
    }

    public Task<SdkUpgradeApplyResult> ApplyVulnerabilityFixesAsync(string anchorPath, CancellationToken ct = default) =>
        _sdk.ApplyAsync(anchorPath, ct);

    const string SdkStrategy = "sdk_dotnet_package_update_vulnerable";

    static int SeverityRank(string severity) =>
        severity.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "moderate" or "medium" => 2,
            "low" => 1,
            _ => 0
        };
}
