#nullable enable

using AIGuiders.Platform.Documentation.Correspondence;

namespace Cdp.ScriptableIde;

/// <summary>
/// CDP façade over platform <see cref="CorrespondenceResolver"/> (GUIDERS-ADR-0028).
/// Keeps <see cref="IdeReport"/> mapping in ScriptableIde.
/// </summary>
public static class WorkspaceCorrespondence
{
    public const string Schema = CorrespondenceSchema.V0;

    public sealed record ForwardDoc(string Path, string Title);

    public sealed record ReverseAnchor(
        string DocPath,
        string DocTitle,
        string Provenance,
        string Kind,
        string File,
        int? LineStart,
        int? LineEnd,
        string? MemberKey,
        string Wire,
        int? DocLineHint = null,
        string? Excerpt = null);

    public sealed record Result(
        string WorkspaceRoot,
        string? FileRel,
        string? FeatureLine,
        string[] FeatureDocs,
        string AdrLine,
        ForwardDoc[] ForwardDocs,
        ReverseAnchor[] ReverseAnchors,
        string[] ActiveLayers,
        string TomlPath);

    public static string? FindWorkspaceRoot(string? startPath, string? hintRoot = null) =>
        CorrespondenceResolver.FindWorkspaceRoot(startPath, hintRoot);

    public static Result? TryResolve(string absoluteFilePath, string? workspaceRootHint = null)
    {
        var platform = CorrespondenceResolver.TryResolve(absoluteFilePath, workspaceRootHint);
        return platform is null ? null : Map(platform);
    }

    public static IdeReport ToIdeReport(CodeAnchor anchor, Result? result)
    {
        if (result is null)
        {
            return new IdeReport
            {
                Kind = "correspondence",
                Available = false,
                Reason = "no_workspace_toml",
                Anchor = IdeReportAnchor.From(anchor),
                Summary = "No .cascade/workspace.toml above this file — open a CIDE-marked repo or pass workspace root.",
                Highlights = [],
                Next = ["cdp_open scm root with .cascade/workspace.toml", "analysis_scene feature=correspondence path="]
            };
        }

        var highlights = new List<IdeReportHighlight>();
        foreach (var d in result.ForwardDocs.Take(12))
            highlights.Add(new IdeReportHighlight { Path = d.Path, Why = $"forward · {d.Title}" });
        foreach (var r in result.ReverseAnchors.Take(8))
            highlights.Add(new IdeReportHighlight { Path = r.Wire, Why = $"reverse · {r.DocTitle} ({r.Provenance})" });

        var summary = string.IsNullOrWhiteSpace(result.FeatureLine)
            ? (result.AdrLine.Length > 0 ? result.AdrLine : "No ADR/feature map for this path")
            : result.FeatureLine + (result.AdrLine.Length > 0 ? " · " + result.AdrLine : "");

        return new IdeReport
        {
            Kind = "correspondence",
            Available = true,
            Anchor = IdeReportAnchor.From(anchor),
            Summary = summary,
            Highlights = highlights,
            Next =
            [
                "analysis_scene feature=correspondence path=",
                "Open forward doc anchor [F:docs/…]",
                "Reverse wire (workspace_toml|bracket|doc_body) → sniper/edit"
            ]
        };
    }

    public static object BuildContext(Result result) =>
        CorrespondenceResolver.BuildContext(MapToPlatform(result));

    public static IdeReport ResolveReport(CodeAnchor anchor, string? workspaceRootHint = null)
    {
        var file = anchor.FilePath;
        if (string.IsNullOrWhiteSpace(file))
        {
            return new IdeReport
            {
                Kind = "correspondence",
                Available = false,
                Reason = "no_file",
                Anchor = IdeReportAnchor.From(anchor),
                Summary = "Correspondence needs CodeAnchor with file path.",
                Highlights = [],
                Next = ["Pass file_path / open buffer"]
            };
        }

        var result = TryResolve(file, workspaceRootHint);
        return ToIdeReport(anchor, result);
    }

    static Result Map(CorrespondenceResult p) => new(
        p.WorkspaceRoot,
        p.FileRel,
        p.FeatureLine,
        p.FeatureDocs,
        p.AdrLine,
        p.ForwardDocs.Select(d => new ForwardDoc(d.Path, d.Title)).ToArray(),
        p.ReverseAnchors.Select(MapReverse).ToArray(),
        p.ActiveLayers,
        p.TomlPath);

    static ReverseAnchor MapReverse(AIGuiders.Platform.Documentation.Correspondence.ReverseAnchor r) => new(
        r.DocPath,
        r.DocTitle,
        r.Provenance,
        r.Kind,
        r.File,
        r.LineStart,
        r.LineEnd,
        r.MemberKey,
        r.Wire,
        r.DocLineHint,
        r.Excerpt);

    static CorrespondenceResult MapToPlatform(Result r) => new(
        r.WorkspaceRoot,
        r.FileRel,
        r.FeatureLine,
        r.FeatureDocs,
        r.AdrLine,
        r.ForwardDocs.Select(d => new AIGuiders.Platform.Documentation.Correspondence.ForwardDoc(d.Path, d.Title)).ToArray(),
        r.ReverseAnchors.Select(MapReverseToPlatform).ToArray(),
        r.ActiveLayers,
        r.TomlPath);

    static AIGuiders.Platform.Documentation.Correspondence.ReverseAnchor MapReverseToPlatform(ReverseAnchor r) => new(
        r.DocPath,
        r.DocTitle,
        r.Provenance,
        r.Kind,
        r.File,
        r.LineStart,
        r.LineEnd,
        r.MemberKey,
        r.Wire,
        r.DocLineHint,
        r.Excerpt);
}
