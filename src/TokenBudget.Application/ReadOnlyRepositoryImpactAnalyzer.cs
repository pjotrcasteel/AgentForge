using TokenBudget.Domain;

namespace TokenBudget.Application;

/// <summary>Reads file metadata only. Does not invoke Git, run build hooks, or follow directory symlinks.</summary>
public sealed class ReadOnlyRepositoryImpactAnalyzer : IRepositoryImpactAnalyzer
{
    private const int MaxFiles = 20_000;
    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj", "packages", "dist", "build", "coverage"
    };

    public Task<RepositoryImpact> InspectAsync(string? repositoryPath, PlanGraph graph, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return Task.FromResult(new RepositoryImpact(false, 0, 0, 0, 0, false, "Repository was not supplied."));
        }

        string root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(new RepositoryImpact(false, 0, 0, 0, 0, false, "Repository directory does not exist."));
        }

        var references = graph.Milestones.SelectMany(static milestone => milestone.ReferencedFiles)
            .Select(static path => Path.GetFileName(path.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int scanned = 0;
        int source = 0;
        int projects = 0;
        int matching = 0;
        bool truncated = false;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ExcludedFolders.Contains(Path.GetFileName(entry)))
                        {
                            pending.Push(entry);
                        }

                        continue;
                    }

                    if (++scanned > MaxFiles)
                    {
                        truncated = true;
                        pending.Clear();
                        break;
                    }

                    string ext = Path.GetExtension(entry);
                    if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        source++;
                    }

                    if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        projects++;
                    }

                    if (references.Contains(Path.GetFileName(entry)))
                    {
                        matching++;
                    }
                }
            }
            catch (IOException)
            {
                truncated = true;
            }
            catch (UnauthorizedAccessException)
            {
                truncated = true;
            }
        }

        return Task.FromResult(new RepositoryImpact(true, Math.Min(scanned, MaxFiles), source, projects, matching,
            truncated, truncated ? "Inspection was partial due to size or inaccessible entries." : null));
    }
}
