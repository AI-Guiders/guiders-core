namespace Cdp.Evidence;

/// <summary>
/// Minimal CDP locus wire compatible with BracketLocate <c>[F:…;L:n]</c> (no Roslyn dependency).
/// </summary>
public static class LocusWire
{
    public static string FileLine(string path, int line1Based, int? column1Based = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (line1Based < 1)
            throw new ArgumentOutOfRangeException(nameof(line1Based));
        // Column is carried on EvidenceItem; wire stays F+L (BracketLocate has no C: axis yet).
        _ = column1Based;
        return $"[F:{path.Trim()};L:{line1Based}]";
    }

    public static string? TryFileLine(string? path, int? line1Based, int? column1Based = null)
    {
        if (string.IsNullOrWhiteSpace(path) || line1Based is null or < 1)
            return null;
        return FileLine(path, line1Based.Value, column1Based);
    }
}
