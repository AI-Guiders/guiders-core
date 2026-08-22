using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AgentFindings.Core;

/// <summary>
/// Serializes JSONL IO per path: in-process gate + named mutex + short IO retry.
/// Parallel MCP tool calls otherwise race on append / read-while-write.
/// </summary>
internal static class JsonlExclusiveAppend
{
    private static readonly ConcurrentDictionary<string, object> LocalGates =
        new(StringComparer.OrdinalIgnoreCase);

    public static void AppendLine(string path, string lineIncludingNewline) =>
        WithExclusive(path, () => AppendLineUnlocked(path, lineIncludingNewline));

    public static void AppendLineUnlocked(string path, string lineIncludingNewline)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(lineIncludingNewline);

        var full = Path.GetFullPath(path);
        EnsureParentDir(full);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    full, FileMode.Append, FileAccess.Write, FileShare.Read,
                    bufferSize: 4096, FileOptions.None);
                var bytes = Encoding.UTF8.GetBytes(lineIncludingNewline);
                fs.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(15 * (attempt + 1));
            }
        }
    }

    public static T WithExclusive<T>(string path, Func<T> action)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(action);

        var full = Path.GetFullPath(path);
        EnsureParentDir(full);

        var local = LocalGates.GetOrAdd(full, static _ => new object());
        lock (local)
        {
            using var mutex = CreatePathMutex(full);
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
                if (!acquired)
                    throw new IOException($"Timeout waiting for exclusive journal lock: {full}");
                return action();
            }
            finally
            {
                if (acquired)
                    mutex.ReleaseMutex();
            }
        }
    }

    public static void WithExclusive(string path, Action action) =>
        WithExclusive(path, () =>
        {
            action();
            return true;
        });

    /// <summary>Read whole file allowing writers with FileShare.Read.</summary>
    public static string[] ReadAllLinesShared(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            return [];

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bufferSize: 4096, FileOptions.SequentialScan);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var list = new List<string>();
                while (reader.ReadLine() is { } line)
                    list.Add(line);
                return list.ToArray();
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(15 * (attempt + 1));
            }
        }
    }

    private static void EnsureParentDir(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static Mutex CreatePathMutex(string fullPath)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())))[..24];
        return new Mutex(initiallyOwned: false, name: @"Local\AgentJournalAppend_" + hash);
    }
}
