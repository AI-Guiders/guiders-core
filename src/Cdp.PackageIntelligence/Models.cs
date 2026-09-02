namespace Cdp.PackageIntelligence;

/// <summary>One vulnerable package reference from dotnet list --vulnerable JSON.</summary>
public sealed record VulnerablePackage(
    string PackageId,
    string RequestedVersion,
    string ResolvedVersion,
    string Framework,
    string ProjectPath,
    bool IsTransitive,
    IReadOnlyList<VulnerabilityAdvisory> Advisories);

public sealed record VulnerabilityAdvisory(string Severity, string AdvisoryUrl);

public sealed record VulnerabilityAuditResult(
    string AnchorPath,
    bool HasVulnerabilities,
    IReadOnlyList<VulnerablePackage> Packages,
    IReadOnlyList<string> Sources);

public sealed record LatestVersionResult(
    string PackageId,
    string? LatestVersion,
    string? LatestStableVersion,
    bool IncludePrerelease,
    string Source);

public sealed record UpgradeAction(
    string PackageId,
    string CurrentVersion,
    string? TargetVersion,
    string Severity,
    string ProjectPath,
    bool IsTransitive,
    string Rationale);

public sealed record UpgradePlanResult(
    string AnchorPath,
    bool HasVulnerabilities,
    IReadOnlyList<UpgradeAction> Actions,
    string Note,
    string Strategy = "sdk_dotnet_package_update_vulnerable",
    string? ApplyCommand = null);

public sealed record PackageSourceInfo(string Name, string Source, bool IsEnabled);

public sealed record SupplyChainReviewResult(
    string RootDirectory,
    bool CentralPackageManagement,
    string? DirectoryPackagesPropsPath,
    IReadOnlyList<PackageSourceInfo> Sources,
    IReadOnlyList<string> Observations);
