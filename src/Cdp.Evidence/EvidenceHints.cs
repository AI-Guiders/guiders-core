namespace Cdp.Evidence;

public static class EvidenceHints
{
    public static string? ForCode(string? code, string message)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
            return null;

        if (code is "CS0103" && message.Contains("Report", StringComparison.Ordinal))
            return "No Report global — return a value or use Help.Toc(); Explore via Symbol/SemanticMap.";
        if (code is "CS1061" && message.Contains("SymbolFacade", StringComparison.Ordinal))
            return "Symbol has Named/FindUsages — not SearchAsync. Try Help.Of(\"Symbol\").";
        if (code is "CS1061" && message.Contains("SemanticMapFacade", StringComparison.Ordinal))
            return "SemanticMap.Explore(anchor) requires NamedCodeAnchor/CodeAnchor. Try Help.Of(\"SemanticMap\").";
        if (code is "CS1501" or "CS1503")
            return "Check overloads via Help.Of(\"…\") before inventing args.";
        if (code is "CS0246")
            return "Missing type — check usings / PackageReference / project reference.";
        if (code is "NU1101" or "NU1102")
            return "Package restore — check feed / PackageReference version.";

        return null;
    }
}
