namespace TerminalMcp.Core;

/// <summary>
/// Side-channel marks in shell stdout/stderr (cwd + env delta).
/// Fragile if the user prints the same prefixes — accepted tradeoff for one-shot processes (kj-1550).
/// </summary>
internal static class ShellMarks
{
    public const int Utf8CodePage = 65001;
    public const int MaxEnvValueChars = 8_192;

    public const string CwdMark = "__CDP_SHELL_CWD__=";
    public const string EnvMark = "__CDP_SHELL_ENV__";
    public const string EnvUnsetMark = "__CDP_SHELL_ENVUNSET__";

    public readonly record struct Peeled(
        string Stdout,
        string Stderr,
        string? FinalCwd,
        IReadOnlyList<KeyValuePair<string, string>> EnvSets,
        IReadOnlyList<string> EnvUnsets);

    public static Peeled Peel(string stdout, string stderr)
    {
        string? cwd = null;
        var envSets = new List<KeyValuePair<string, string>>();
        var envUnsets = new List<string>();

        string PeelStream(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');
            var kept = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                var cwdIdx = line.IndexOf(CwdMark, StringComparison.Ordinal);
                if (cwdIdx >= 0)
                {
                    cwd = line[(cwdIdx + CwdMark.Length)..].Trim();
                    continue;
                }

                var unsetIdx = line.IndexOf(EnvUnsetMark, StringComparison.Ordinal);
                if (unsetIdx >= 0)
                {
                    var name = line[(unsetIdx + EnvUnsetMark.Length)..].Trim();
                    if (name.Length > 0)
                        envUnsets.Add(name);
                    continue;
                }

                var envIdx = line.IndexOf(EnvMark, StringComparison.Ordinal);
                if (envIdx >= 0)
                {
                    var payload = line[(envIdx + EnvMark.Length)..];
                    var eq = payload.IndexOf('=');
                    if (eq > 0)
                    {
                        var name = payload[..eq];
                        var value = payload[(eq + 1)..];
                        if (name.Length > 0 && value.Length <= MaxEnvValueChars)
                            envSets.Add(new KeyValuePair<string, string>(name, value));
                    }

                    continue;
                }

                kept.Add(line);
            }

            if (kept.Count == 0)
                return "";
            if (kept[^1].Length == 0)
                kept.RemoveAt(kept.Count - 1);
            return kept.Count == 0 ? "" : string.Join("\n", kept) + "\n";
        }

        return new Peeled(PeelStream(stdout), PeelStream(stderr), cwd, envSets, envUnsets);
    }
}
