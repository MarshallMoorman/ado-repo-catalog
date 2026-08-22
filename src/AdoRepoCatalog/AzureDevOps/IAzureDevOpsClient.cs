namespace AdoRepoCatalog.AzureDevOps;

public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<AdoProject>> ListProjectsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoRepository>> ListRepositoriesAsync(
        string project,
        CancellationToken cancellationToken = default);

    Task<AdoRepository> GetRepositoryAsync(
        string project,
        string repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the HEAD commit SHA for <paramref name="branch"/>, or null if the branch has no commits.</summary>
    Task<string?> GetHeadCommitAsync(
        string project,
        string repositoryId,
        string branch,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoItem>> ListItemsAsync(
        string project,
        string repositoryId,
        string scopePath,
        string branch,
        CancellationToken cancellationToken = default);

    Task<string?> GetItemContentAsync(
        string project,
        string repositoryId,
        string path,
        string branch,
        CancellationToken cancellationToken = default);
}
