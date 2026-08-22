using System.Text.Json;
using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Embedding;

namespace AdoRepoCatalog.Catalog;

public sealed class CatalogRunner
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CatalogOptions _options;
    private readonly IAzureDevOpsClient _client;
    private readonly IIndexStateStore _stateStore;
    private readonly ICatalogEmbedder _embedder;
    private readonly TimeProvider _clock;
    private readonly TextWriter _log;

    public CatalogRunner(
        CatalogOptions options,
        IAzureDevOpsClient client,
        IIndexStateStore stateStore,
        ICatalogEmbedder? embedder = null,
        TimeProvider? timeProvider = null,
        TextWriter? log = null)
    {
        _options = options;
        _client = client;
        _stateStore = stateStore;
        _embedder = embedder ?? NoOpCatalogEmbedder.Instance;
        _clock = timeProvider ?? TimeProvider.System;
        _log = log ?? Console.Out;
    }

    public async Task<CatalogRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.OutputDirectory);
        Directory.CreateDirectory(_options.WikiDirectory);

        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var projects = await ResolveProjectsAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<CatalogEntry>();
        var indexed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var project in projects)
        {
            IReadOnlyList<AdoRepository> repositories;
            try
            {
                repositories = await _client.ListRepositoriesAsync(project.Name, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                await _log.WriteLineAsync($"[error] list repos for {project.Name}: {ex.Message}").ConfigureAwait(false);
                continue;
            }

            foreach (var listed in repositories)
            {
                if (listed.IsDisabled)
                {
                    await _log.WriteLineAsync($"[skip-disabled] {project.Name}/{listed.Name}").ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var outcome = await ProcessRepositoryAsync(project, listed, state, cancellationToken)
                        .ConfigureAwait(false);
                    entries.Add(outcome.Entry);
                    if (outcome.Skipped)
                    {
                        skipped++;
                    }
                    else
                    {
                        indexed++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    await _log.WriteLineAsync($"[error] {project.Name}/{listed.Name}: {ex.Message}")
                        .ConfigureAwait(false);
                }
            }
        }

        entries.Sort((a, b) =>
        {
            var project = string.Compare(a.Project, b.Project, StringComparison.OrdinalIgnoreCase);
            return project != 0 ? project : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        await WriteCatalogJsonAsync(entries, cancellationToken).ConfigureAwait(false);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        await _embedder.EmbedAsync(entries, cancellationToken).ConfigureAwait(false);

        await _log.WriteLineAsync(
                $"[done] indexed={indexed} skipped={skipped} failed={failed} catalog={_options.CatalogJsonPath}")
            .ConfigureAwait(false);

        return new CatalogRunResult
        {
            CatalogJsonPath = Path.GetFullPath(_options.CatalogJsonPath),
            WikiDirectory = Path.GetFullPath(_options.WikiDirectory),
            Entries = entries,
            IndexedCount = indexed,
            SkippedUnchangedCount = skipped,
            FailedCount = failed,
        };
    }

    private async Task<IReadOnlyList<AdoProject>> ResolveProjectsAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.Project))
        {
            return [new AdoProject { Name = _options.Project.Trim() }];
        }

        var projects = await _client.ListProjectsAsync(cancellationToken).ConfigureAwait(false);
        return projects
            .Where(project =>
                string.IsNullOrWhiteSpace(project.State) ||
                project.State.Equals("wellFormed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private async Task<(CatalogEntry Entry, bool Skipped)> ProcessRepositoryAsync(
        AdoProject project,
        AdoRepository listed,
        IndexState state,
        CancellationToken cancellationToken)
    {
        var details = await _client.GetRepositoryAsync(project.Name, listed.Id, cancellationToken)
            .ConfigureAwait(false);
        details.Project ??= project;
        if (string.IsNullOrWhiteSpace(details.Project.Name))
        {
            details.Project.Name = project.Name;
        }

        var resolved = await BranchResolver.ResolveAsync(
                _client,
                project.Name,
                details.Id,
                details.DefaultBranch,
                cancellationToken)
            .ConfigureAwait(false);

        var branch = resolved?.Name ?? BranchResolver.StripRefsHeads(details.DefaultBranch) ?? "";
        var headSha = resolved?.Sha ?? "";
        var wikiFileName = WikiPageWriter.FileName(project.Name, details.Name);
        var wikiFullPath = Path.Combine(_options.WikiDirectory, wikiFileName);
        var relativeWikiPath = Path.Combine("wiki", wikiFileName).Replace('\\', '/');

        if (ShouldSkip(state, details.Id, headSha, wikiFullPath))
        {
            await _log.WriteLineAsync($"[skip] {project.Name}/{details.Name} @ {headSha}").ConfigureAwait(false);
            if (WikiPageWriter.TryReadEntry(wikiFullPath, relativeWikiPath, out var existing))
            {
                return (existing, Skipped: true);
            }
        }

        await _log.WriteLineAsync($"[index] {project.Name}/{details.Name} branch={branch} sha={headSha}")
            .ConfigureAwait(false);

        var snapshot = await FetchSnapshotAsync(project.Name, details, branch, headSha, cancellationToken)
            .ConfigureAwait(false);
        var entry = RepoInference.Infer(snapshot, _clock.GetUtcNow());
        entry.WikiPath = relativeWikiPath;
        if (string.IsNullOrWhiteSpace(entry.DefaultBranch))
        {
            entry.DefaultBranch = branch;
        }

        var previous = File.Exists(wikiFullPath) ? File.ReadAllText(wikiFullPath) : null;
        WikiPageWriter.Write(_options.WikiDirectory, entry, previous);

        state.Repos[details.Id] = new IndexedRepoState
        {
            HeadSha = headSha,
            WikiFileName = wikiFileName,
            IndexedAt = entry.LastIndexed,
        };

        return (entry, Skipped: false);
    }

    private static bool ShouldSkip(IndexState state, string repoId, string headSha, string wikiFullPath)
    {
        if (!File.Exists(wikiFullPath))
        {
            return false;
        }

        if (!state.Repos.TryGetValue(repoId, out var previous))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(headSha) || string.IsNullOrWhiteSpace(previous.HeadSha))
        {
            return false;
        }

        return string.Equals(previous.HeadSha, headSha, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RepoSnapshot> FetchSnapshotAsync(
        string project,
        AdoRepository repository,
        string branch,
        string headSha,
        CancellationToken cancellationToken)
    {
        var tree = new List<AdoItem>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(branch))
        {
            return new RepoSnapshot
            {
                Repository = repository,
                Branch = branch,
                HeadSha = headSha,
                TreeEntries = tree,
                Files = files,
            };
        }

        var root = await _client.ListItemsAsync(project, repository.Id, "/", branch, cancellationToken)
            .ConfigureAwait(false);
        tree.AddRange(root);

        var extraFolder = KeyFileSelector.ChooseShallowFolder(root);
        if (extraFolder is not null)
        {
            var extra = await _client.ListItemsAsync(
                    project,
                    repository.Id,
                    "/" + extraFolder,
                    branch,
                    cancellationToken)
                .ConfigureAwait(false);
            tree.AddRange(extra);
        }

        foreach (var path in KeyFileSelector.SelectFetchPaths(tree))
        {
            var content = await _client.GetItemContentAsync(project, repository.Id, path, branch, cancellationToken)
                .ConfigureAwait(false);
            if (content is not null)
            {
                files[path] = content;
            }
        }

        return new RepoSnapshot
        {
            Repository = repository,
            Branch = branch,
            HeadSha = headSha,
            TreeEntries = tree,
            Files = files,
        };
    }

    private async Task WriteCatalogJsonAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_options.CatalogJsonPath);
        await JsonSerializer.SerializeAsync(stream, entries, CatalogJsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
