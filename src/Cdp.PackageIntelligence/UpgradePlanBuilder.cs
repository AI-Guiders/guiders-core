namespace Cdp.PackageIntelligence;

/// <summary>Build agent-actionable upgrade plans from vulnerability audit + feed metadata.</summary>
public sealed class UpgradePlanBuilder
{
    private readonly VulnerabilityAuditor _auditor;
    private readonly NuGetMetadataClient _metadata;

    public UpgradePlanBuilder(VulnerabilityAuditor? auditor = null, NuGetMetadataClient? metadata = null)
    {
        _auditor = auditor ?? new VulnerabilityAuditor();
        _metadata = metadata ?? new NuGetMetadataClient();
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
                "no_vulnerabilities");
        }

        var actions = new List<UpgradeAction>();
        foreach (var pkg in audit.Packages)
        {
            var severity = pkg.Advisories
                .Select(a => a.Severity)
                .OrderByDescending(SeverityRank)
                .FirstOrDefault() ?? "Unknown";

            var target = await _metadata.ResolveUpgradeTargetAsync(
                pkg.PackageId,
                pkg.ResolvedVersion,
                includePrerelease,
                ct).ConfigureAwait(false);

            var rationale = target is null
                ? "no_newer_version_on_feed"
                : "upgrade_to_latest_compatible_on_feed";

            actions.Add(new UpgradeAction(
                pkg.PackageId,
                pkg.ResolvedVersion,
                target,
                severity,
                pkg.ProjectPath,
                pkg.IsTransitive,
                rationale));
        }

        return new UpgradePlanResult(
            audit.AnchorPath,
            true,
            actions,
            "apply_via_cdp_pkg_update_or_edit_csproj; graph_solver_not_in_v1");
    }

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
