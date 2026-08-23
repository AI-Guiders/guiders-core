using System.Text.Json;

namespace Cdp.PackageIntelligence.Internal;

internal static class VulnerableListJsonParser
{
    public static VulnerabilityAuditResult Parse(string anchorPath, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sources = root.TryGetProperty("sources", out var srcEl) && srcEl.ValueKind == JsonValueKind.Array
            ? srcEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

        var packages = new List<VulnerablePackage>();
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var project in projects.EnumerateArray())
            {
                var projectPath = project.TryGetProperty("path", out var pp) ? pp.GetString() ?? anchorPath : anchorPath;
                if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var fw in frameworks.EnumerateArray())
                {
                    var framework = fw.TryGetProperty("framework", out var fEl) ? fEl.GetString() ?? "" : "";
                    ParsePackageList(projectPath, framework, fw, "topLevelPackages", isTransitive: false, packages);
                    ParsePackageList(projectPath, framework, fw, "transitivePackages", isTransitive: true, packages);
                }
            }
        }

        return new VulnerabilityAuditResult(
            anchorPath,
            packages.Count > 0,
            packages,
            sources);
    }

    static void ParsePackageList(
        string projectPath,
        string framework,
        JsonElement fw,
        string propertyName,
        bool isTransitive,
        List<VulnerablePackage> sink)
    {
        if (!fw.TryGetProperty(propertyName, out var list) || list.ValueKind != JsonValueKind.Array)
            return;

        foreach (var pkg in list.EnumerateArray())
        {
            if (!pkg.TryGetProperty("vulnerabilities", out var vulns) || vulns.ValueKind != JsonValueKind.Array
                || vulns.GetArrayLength() == 0)
                continue;

            var id = pkg.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (id.Length == 0)
                continue;

            var requested = pkg.TryGetProperty("requestedVersion", out var rq) ? rq.GetString() ?? "" : "";
            var resolved = pkg.TryGetProperty("resolvedVersion", out var rs) ? rs.GetString() ?? "" : "";
            var advisories = vulns.EnumerateArray().Select(v => new VulnerabilityAdvisory(
                v.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "Unknown" : "Unknown",
                v.TryGetProperty("advisoryurl", out var url) ? url.GetString() ?? ""
                    : v.TryGetProperty("advisoryUrl", out var url2) ? url2.GetString() ?? "" : ""
            )).ToArray();

            sink.Add(new VulnerablePackage(id, requested, resolved, framework, projectPath, isTransitive, advisories));
        }
    }
}
