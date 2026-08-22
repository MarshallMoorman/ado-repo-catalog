using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Tests;

public sealed class FakeAzureDevOpsClient : IAzureDevOpsClient
{
    public List<AdoProject> Projects { get; } = [];

    public List<AdoRepository> Repositories { get; } = [];

    public Dictionary<string, Dictionary<string, string>> HeadByRepoBranch { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, List<AdoItem>>> ItemsByRepoBranchScope { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> ContentByRepoBranchPath { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int ListProjectsCalls { get; private set; }

    public int ListRepositoriesCalls { get; private set; }

    public int GetRepositoryCalls { get; private set; }

    public int GetHeadCommitCalls { get; private set; }

    public int ListItemsCalls { get; private set; }

    public int GetItemContentCalls { get; private set; }

    public Task<IReadOnlyList<AdoProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        ListProjectsCalls++;
        return Task.FromResult<IReadOnlyList<AdoProject>>(Projects.ToArray());
    }

    public Task<IReadOnlyList<AdoRepository>> ListRepositoriesAsync(
        string project,
        CancellationToken cancellationToken = default)
    {
        ListRepositoriesCalls++;
        var matches = Repositories
            .Where(repo => string.Equals(repo.Project?.Name, project, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult<IReadOnlyList<AdoRepository>>(matches);
    }

    public Task<AdoRepository> GetRepositoryAsync(
        string project,
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        GetRepositoryCalls++;
        var repo = Repositories.First(item =>
            string.Equals(item.Id, repositoryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Project?.Name, project, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(Clone(repo));
    }

    public Task<string?> GetHeadCommitAsync(
        string project,
        string repositoryId,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GetHeadCommitCalls++;
        if (HeadByRepoBranch.TryGetValue(repositoryId, out var branches) &&
            branches.TryGetValue(branch, out var sha))
        {
            return Task.FromResult<string?>(sha);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<AdoItem>> ListItemsAsync(
        string project,
        string repositoryId,
        string scopePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ListItemsCalls++;
        var key = ScopeKey(branch, scopePath);
        if (ItemsByRepoBranchScope.TryGetValue(repositoryId, out var scopes) &&
            scopes.TryGetValue(key, out var items))
        {
            return Task.FromResult<IReadOnlyList<AdoItem>>(items.ToArray());
        }

        return Task.FromResult<IReadOnlyList<AdoItem>>([]);
    }

    public Task<string?> GetItemContentAsync(
        string project,
        string repositoryId,
        string path,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GetItemContentCalls++;
        if (ContentByRepoBranchPath.TryGetValue(repositoryId, out var branches) &&
            branches.TryGetValue(branch, out var files))
        {
            var match = files.FirstOrDefault(pair =>
                string.Equals(Normalize(pair.Key), Normalize(path), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key))
            {
                return Task.FromResult<string?>(match.Value);
            }
        }

        return Task.FromResult<string?>(null);
    }

    public void SetHead(string repositoryId, string branch, string sha)
    {
        if (!HeadByRepoBranch.TryGetValue(repositoryId, out var branches))
        {
            branches = new Dictionary<string, string>(StringComparer.Ordinal);
            HeadByRepoBranch[repositoryId] = branches;
        }

        branches[branch] = sha;
    }

    public void AddItems(string repositoryId, string branch, string scopePath, params AdoItem[] items)
    {
        if (!ItemsByRepoBranchScope.TryGetValue(repositoryId, out var scopes))
        {
            scopes = new Dictionary<string, List<AdoItem>>(StringComparer.OrdinalIgnoreCase);
            ItemsByRepoBranchScope[repositoryId] = scopes;
        }

        scopes[ScopeKey(branch, scopePath)] = items.ToList();
    }

    public void AddFile(string repositoryId, string branch, string path, string content)
    {
        if (!ContentByRepoBranchPath.TryGetValue(repositoryId, out var branches))
        {
            branches = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            ContentByRepoBranchPath[repositoryId] = branches;
        }

        if (!branches.TryGetValue(branch, out var files))
        {
            files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            branches[branch] = files;
        }

        files[Normalize(path)] = content;
    }

    public void ResetCallCounts()
    {
        ListProjectsCalls = 0;
        ListRepositoriesCalls = 0;
        GetRepositoryCalls = 0;
        GetHeadCommitCalls = 0;
        ListItemsCalls = 0;
        GetItemContentCalls = 0;
    }

    private static AdoRepository Clone(AdoRepository repo) => new()
    {
        Id = repo.Id,
        Name = repo.Name,
        DefaultBranch = repo.DefaultBranch,
        RemoteUrl = repo.RemoteUrl,
        WebUrl = repo.WebUrl,
        IsDisabled = repo.IsDisabled,
        Project = repo.Project is null
            ? null
            : new AdoProject
            {
                Id = repo.Project.Id,
                Name = repo.Project.Name,
                Description = repo.Project.Description,
                State = repo.Project.State,
            },
    };

    private static string ScopeKey(string branch, string scopePath) => $"{branch}|{Normalize(scopePath)}";

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return path.StartsWith('/') ? path : "/" + path;
    }
}
