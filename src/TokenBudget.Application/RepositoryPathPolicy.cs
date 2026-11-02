namespace TokenBudget.Application;

/// <summary>
/// Restricts repository inspection for remotely supplied paths. CLI callers remain responsible
/// for selecting their own local path; server entry points should validate paths with this policy.
/// </summary>
public static class RepositoryPathPolicy
{
    public static string? Resolve(string? requestedPath, string? allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(allowedRoot))
        {
            throw new InvalidOperationException("Repository inspection is disabled on this server.");
        }

        string root = Path.GetFullPath(allowedRoot);
        string selected = Path.GetFullPath(Path.IsPathRooted(requestedPath)
            ? requestedPath
            : Path.Combine(root, requestedPath));

        string relative = Path.GetRelativePath(root, selected);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new UnauthorizedAccessException("Repository path is outside the configured root.");
        }

        // Do not allow a symlink component to escape the root between validation and inspection.
        string cursor = Path.GetPathRoot(selected)!;
        foreach (string part in selected[cursor.Length..].Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            if ((Directory.Exists(cursor) || File.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Repository path contains a symbolic link.");
            }
        }

        if (!Directory.Exists(selected))
        {
            throw new DirectoryNotFoundException("Repository directory was not found.");
        }

        return selected;
    }
}
