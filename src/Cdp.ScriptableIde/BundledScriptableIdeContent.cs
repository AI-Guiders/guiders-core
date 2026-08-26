using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Cdp.ScriptableIde;

/// <summary>Embedded resources (see <c>EmbeddedResource</c> in csproj).</summary>
internal static class BundledScriptableIdeContent
{
    private static readonly Assembly s_assembly = typeof(BundledScriptableIdeContent).Assembly;
    private const string ResourcePrefix = "Cdp.ScriptableIde.Resources.";

    internal static bool TryReadEmbeddedText(string relativePath, [NotNullWhen(true)] out string? text)
    {
        text = null;
        var normalized = relativePath.Replace('\\', '/').TrimStart('/').Trim();
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal))
            return false;

        var name = ResourcePrefix + normalized.Replace('/', '.');
        using var stream = s_assembly.GetManifestResourceStream(name);
        if (stream is null)
            return false;
        using var reader = new StreamReader(stream);
        text = reader.ReadToEnd();
        return !string.IsNullOrWhiteSpace(text);
    }
}
