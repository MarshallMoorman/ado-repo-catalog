using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class CatalogRunnerEdgeTests
{
    [Fact]
    public async Task Skips_disabled_repos_and_continues_after_list_failure()
    {
        var client = new FakeAzureDevOpsClient();
        client.Projects.Add(new AdoProject { Name = "Broken", State = "wellFormed" });
        client.Projects.Add(new AdoProject
        {
            Id = FabrikamFixture.ProjectId,
            Name = FabrikamFixture.ProjectName,
            State = "wellFormed",
        });
        client.Repositories.Add(new AdoRepository
        {
            Id = FabrikamFixture.ContosoDemoId,
            Name = "contoso-demo",
            DefaultBranch = "refs/heads/main",
            RemoteUrl = "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
            IsDisabled = true,
            Project = client.Projects[1],
        });

        var throwing = new ThrowingListClient(client, failProject: "Broken");
        using var host = new CatalogTestHost(throwing);
        var result = await host.Runner.RunAsync();

        Assert.Equal(1, result.FailedCount);
        Assert.Empty(result.Entries);
        Assert.Contains("skip-disabled", host.Log.ToString());
    }

    [Fact]
    public async Task File_state_store_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), "ado-catalog-state-" + Guid.NewGuid().ToString("N"), "state.json");
        var store = new FileIndexStateStore(path);
        var loaded = await store.LoadAsync();
        Assert.Empty(loaded.Repos);

        loaded.Repos["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"] = new IndexedRepoState
        {
            HeadSha = "1111111111111111111111111111111111111111",
            Branch = "develop",
            WikiFileName = "fabrikam-fiber-git-contoso-demo.md",
        };
        await store.SaveAsync(loaded);
        var again = await store.LoadAsync();
        var stored = again.Repos["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"];
        Assert.Equal("1111111111111111111111111111111111111111", stored.HeadSha);
        Assert.Equal("develop", stored.Branch);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private sealed class ThrowingListClient : IAzureDevOpsClient
    {
        private readonly FakeAzureDevOpsClient _inner;
        private readonly string _failProject;

        public ThrowingListClient(FakeAzureDevOpsClient inner, string failProject)
        {
            _inner = inner;
            _failProject = failProject;
        }

        public Task<IReadOnlyList<AdoProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
            => _inner.ListProjectsAsync(cancellationToken);

        public Task<IReadOnlyList<AdoRepository>> ListRepositoriesAsync(string project, CancellationToken cancellationToken = default)
        {
            if (string.Equals(project, _failProject, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("fixture list failure");
            }

            return _inner.ListRepositoriesAsync(project, cancellationToken);
        }

        public Task<AdoRepository> GetRepositoryAsync(string project, string repositoryId, CancellationToken cancellationToken = default)
            => _inner.GetRepositoryAsync(project, repositoryId, cancellationToken);

        public Task<string?> GetHeadCommitAsync(string project, string repositoryId, string branch, CancellationToken cancellationToken = default)
            => _inner.GetHeadCommitAsync(project, repositoryId, branch, cancellationToken);

        public Task<IReadOnlyList<AdoItem>> ListItemsAsync(string project, string repositoryId, string scopePath, string branch, CancellationToken cancellationToken = default)
            => _inner.ListItemsAsync(project, repositoryId, scopePath, branch, cancellationToken);

        public Task<string?> GetItemContentAsync(string project, string repositoryId, string path, string branch, CancellationToken cancellationToken = default)
            => _inner.GetItemContentAsync(project, repositoryId, path, branch, cancellationToken);
    }
}
