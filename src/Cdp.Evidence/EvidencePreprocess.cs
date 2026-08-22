using System.Text.Json;

namespace Cdp.Evidence;

/// <summary>
/// Locus projection: raw pipe / structured diags → <c>evidence/v0</c> with Anchor wires.
/// Shared agent + human surface (kj-20260724-1238).
/// </summary>
public static class EvidencePreprocess
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Project text for a known or auto-detected source kind.</summary>
    public static EvidenceDocument Project(string kind, string? text, EvidenceContext? ctx = null)
    {
        ctx ??= new EvidenceContext();
        var source = ParseKind(kind);
        text ??= "";

        if (source == EvidenceSource.Auto)
            source = DetectSource(text);

        var items = source switch
        {
            EvidenceSource.Build or EvidenceSource.Publish => MsBuildExtractor.Extract(text, ctx).ToList(),
            EvidenceSource.Test => ProjectTestText(text, ctx),
            EvidenceSource.Shell or EvidenceSource.Generic or EvidenceSource.Auto => ProjectShellish(text, ctx),
            EvidenceSource.Csx or EvidenceSource.Roslyn => MsBuildExtractor.Extract(text, ctx)
                .Concat(StackTraceExtractor.Extract(text, ctx)).ToList(),
            _ => ProjectShellish(text, ctx)
        };

        return Finalize(source, items, text, ctx);
    }

    public static EvidenceDocument Project(EvidenceSource source, string? text, EvidenceContext? ctx = null) =>
        Project(EvidenceDocument.SourceName(source), text, ctx);

    /// <summary>From already-parsed MSBuild diagnostics (build/publish structured path).</summary>
    public static EvidenceDocument FromBuildDiagnostics(
        IEnumerable<(string File, int Line, int? Column, string? Code, string Message, bool IsError)> diagnostics,
        EvidenceContext? ctx = null,
        EvidenceSource source = EvidenceSource.Build)
    {
        ctx ??= new EvidenceContext();
        var items = new List<EvidenceItem>();
        foreach (var d in diagnostics)
        {
            var path = MsBuildExtractor.NormalizePath(d.File, ctx);
            items.Add(new EvidenceItem(
                Severity: d.IsError ? "error" : "warning",
                Message: d.Message,
                Id: d.Code,
                Path: path,
                Line: d.Line,
                Column: d.Column,
                Anchor: LocusWire.TryFileLine(path, d.Line, d.Column),
                Hint: EvidenceHints.ForCode(d.Code, d.Message),
                Title: d.Code));
        }

        return Finalize(source, items, residualSource: null, ctx);
    }

    /// <summary>From failed test rows (name + message).</summary>
    public static EvidenceDocument FromFailedTests(
        IEnumerable<(string Name, string? Message)> failed,
        EvidenceContext? ctx = null,
        string? rawOutput = null)
    {
        ctx ??= new EvidenceContext();
        var items = failed.Select(t => StackTraceExtractor.FromFailedTest(t.Name, t.Message, ctx)).ToList();
        if (rawOutput is { Length: > 0 })
        {
            foreach (var extra in StackTraceExtractor.Extract(rawOutput, ctx))
            {
                if (items.Any(i => i.Anchor == extra.Anchor && i.Path == extra.Path))
                    continue;
                items.Add(extra);
            }

            foreach (var buildish in MsBuildExtractor.Extract(rawOutput, ctx))
            {
                var isError = buildish.Severity.Equals("error", StringComparison.OrdinalIgnoreCase);
                if (!isError && !ctx.IncludeWarnings)
                    continue;
                if (items.Any(i => i.Anchor == buildish.Anchor))
                    continue;
                items.Add(buildish);
            }
        }

        return Finalize(EvidenceSource.Test, items, rawOutput, ctx);
    }

    /// <summary>From CSX / Roslyn-style items already shaped (fold into evidence/v0).</summary>
    public static EvidenceDocument FromCsxItems(
        IEnumerable<(string Id, string Severity, string Message, int? Line, int? Column, string? Anchor, string? Hint)> items,
        EvidenceContext? ctx = null)
    {
        ctx ??= new EvidenceContext();
        var list = items.Select(i => new EvidenceItem(
            Severity: i.Severity,
            Message: i.Message,
            Id: i.Id,
            Path: "<csx>",
            Line: i.Line,
            Column: i.Column,
            Anchor: i.Anchor ?? LocusWire.TryFileLine("<csx>", i.Line, i.Column),
            Hint: i.Hint ?? EvidenceHints.ForCode(i.Id, i.Message),
            Title: i.Id)).ToList();
        return Finalize(EvidenceSource.Csx, list, residualSource: null, ctx);
    }

    public static string ToJson(EvidenceDocument doc) =>
        JsonSerializer.Serialize(ToDto(doc), JsonOpts);

    public static EvidenceDocumentDto ToDto(EvidenceDocument doc) => new()
    {
        Schema = doc.Schema,
        Source = doc.Source,
        Ok = doc.Ok,
        ItemCount = doc.ItemCount,
        Residual = doc.Residual,
        Note = doc.Note,
        Items = doc.Items.Select(i => new EvidenceItemDto
        {
            Id = i.Id,
            Severity = i.Severity,
            Message = i.Message,
            Path = i.Path,
            Line = i.Line,
            Column = i.Column,
            Anchor = i.Anchor,
            Hint = i.Hint,
            Title = i.Title
        }).ToList()
    };

    private static List<EvidenceItem> ProjectTestText(string text, EvidenceContext ctx)
    {
        var items = new List<EvidenceItem>();
        items.AddRange(StackTraceExtractor.Extract(text, ctx));
        items.AddRange(MsBuildExtractor.Extract(text, ctx));
        return items;
    }

    private static List<EvidenceItem> ProjectShellish(string text, EvidenceContext ctx)
    {
        var items = new List<EvidenceItem>();
        items.AddRange(MsBuildExtractor.Extract(text, ctx));
        items.AddRange(StackTraceExtractor.Extract(text, ctx));
        return Dedup(items);
    }

    private static EvidenceDocument Finalize(
        EvidenceSource source,
        List<EvidenceItem> items,
        string? residualSource,
        EvidenceContext ctx)
    {
        items = Dedup(items);
        var errors = items
            .Where(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var warnings = items
            .Where(i => !i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var warningTotal = warnings.Count;
        if (!ctx.IncludeWarnings)
        {
            var warnCap = Math.Clamp(ctx.MaxWarnings, 0, 50);
            warnings = warnings.Take(warnCap).ToList();
        }

        items = errors.Concat(warnings).ToList();
        var max = Math.Clamp(ctx.MaxItems, 1, 500);
        var truncated = items.Count > max;
        if (truncated)
            items = items.Take(max).ToList();

        var errorCount = items.Count(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        string? residual = null;
        if (residualSource is { Length: > 0 } && items.Count == 0)
        {
            var cap = Math.Clamp(ctx.MaxResidualChars, 256, 50_000);
            residual = residualSource.Length <= cap
                ? residualSource
                : residualSource[..cap] + "…";
        }

        string? note = null;
        if (truncated)
            note = $"truncated_to_{max}";
        else if (!ctx.IncludeWarnings && warningTotal > warnings.Count)
            note = $"warnings_omitted_{warningTotal - warnings.Count}";

        return new EvidenceDocument(
            EvidenceSchema.Version,
            EvidenceDocument.SourceName(source),
            Ok: errorCount == 0,
            ItemCount: items.Count,
            Items: items,
            Residual: residual,
            Note: note);
    }

    private static List<EvidenceItem> Dedup(List<EvidenceItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<EvidenceItem>();
        foreach (var i in items)
        {
            var key = $"{i.Severity}|{i.Id}|{i.Anchor}|{i.Message}";
            if (!seen.Add(key)) continue;
            list.Add(i);
        }

        return list;
    }

    private static EvidenceSource ParseKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return EvidenceSource.Auto;
        return kind.Trim().ToLowerInvariant() switch
        {
            "auto" => EvidenceSource.Auto,
            "build" or "msbuild" or "dotnet_build" => EvidenceSource.Build,
            "test" or "dotnet_test" => EvidenceSource.Test,
            "publish" or "dotnet_publish" => EvidenceSource.Publish,
            "csx" => EvidenceSource.Csx,
            "shell" or "stderr" or "stdout" => EvidenceSource.Shell,
            "roslyn" or "diag" => EvidenceSource.Roslyn,
            "generic" => EvidenceSource.Generic,
            _ => EvidenceSource.Auto
        };
    }

    private static EvidenceSource DetectSource(string text)
    {
        if (text.Contains("error CS", StringComparison.OrdinalIgnoreCase)
            || text.Contains("): error ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("): warning ", StringComparison.OrdinalIgnoreCase))
            return EvidenceSource.Build;
        if (text.Contains("Failed!", StringComparison.Ordinal)
            || text.Contains("Error Message:", StringComparison.Ordinal)
            || text.Contains("Stack Trace:", StringComparison.Ordinal))
            return EvidenceSource.Test;
        return EvidenceSource.Shell;
    }
}
