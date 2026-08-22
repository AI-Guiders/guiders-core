namespace GitMcp.Core;

/// <summary>
/// Определение корня git worktree: обычный репозиторий (<c>.git</c> — каталог) или субмодуль/secondary worktree (<c>.git</c> — файл с <c>gitdir:</c>).
/// </summary>
public static class GitWorkTree
{
    /// <summary>
    /// Нормализует путь и проверяет, что это корень рабочего дерева git.
    /// </summary>
    /// <exception cref="ArgumentException">В каталоге нет <c>.git</c> (ни каталога, ни файла).</exception>
    public static string GetRepoRoot(string repoRoot)
    {
        var root = Path.GetFullPath(repoRoot.Trim());
        if (File.Exists(root))
            root = Path.GetDirectoryName(root) ?? root;
        if (!IsGitWorkTreeRoot(root))
            throw new ArgumentException($"Not a git repository: {root}");
        return root;
    }

    /// <summary>
    /// true, если в <paramref name="directory"/> есть <c>.git</c> как каталог (обычный clone/init) или как файл (субмодуль, git worktree add).
    /// </summary>
    public static bool IsGitWorkTreeRoot(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        if (Directory.Exists(gitPath))
            return true;
        if (File.Exists(gitPath))
            return true;
        return false;
    }
}
