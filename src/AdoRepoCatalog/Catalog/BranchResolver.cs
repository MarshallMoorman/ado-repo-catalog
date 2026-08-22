using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Catalog;

public static class BranchResolver
{
    public static readonly string[] FallbackOrder =
    [
        "develop", "dev", "Develop", "Dev", "main", "master", "Main", "Master",
    ];

    public static string? StripRefsHeads(string? defaultBranch)
    {
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            return null;
        }

        const string prefix = "refs/heads/";
        return defaultBranch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? defaultBranch[prefix.Length..]
            : defaultBranch;
    }

    public static IReadOnlyList<string> Candidates(string? defaultBranch)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                return;
            }

            list.Add(name);
        }

        Add(StripRefsHeads(defaultBranch));
        foreach (var fallback in FallbackOrder)
        {
            Add(fallback);
        }

        return list;
    }

    public static async Task<ResolvedBranch?> ResolveAsync(
        IAzureDevOpsClient client,
        string project,
        string repositoryId,
        string? defaultBranch,
        CancellationToken cancellationToken = default)
    {
        foreach (var branch in Candidates(defaultBranch))
        {
            var sha = await client.GetHeadCommitAsync(project, repositoryId, branch, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sha))
            {
                return new ResolvedBranch(branch, sha);
            }

            var items = await client.ListItemsAsync(project, repositoryId, "/", branch, cancellationToken)
                .ConfigureAwait(false);
            if (items.Count > 0)
            {
                return new ResolvedBranch(branch, "");
            }
        }

        return null;
    }
}

public sealed record ResolvedBranch(string Name, string Sha);
