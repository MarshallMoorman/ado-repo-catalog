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
        var gate = new object();
        var work = new List<(AdoProject Project, AdoRepository Repository)>();

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
                Interlocked.Increment(ref failed);
                WriteLog($"[error] list repos for {project.Name}: {ex.Message}", gate);
                continue;
            }

            foreach (var listed in repositories)
            {
                if (listed.IsDisabled)
                {
                    WriteLog($"[skip-disabled] {project.Name}/{listed.Name}", gate);
                    continue;
                }

                work.Add((project, listed));
            }
        }

        await Parallel.ForEachAsync(
            work,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.EffectiveMaxConcurrency,
                CancellationToken = cancellationToken,
            },
            async (item, token) =>
            {
                try
                {
                    var outcome = await ProcessRepositoryAsync(item.Project, item.Repository, state, gate, token)
                        .ConfigureAwait(false);
                    lock (gate)
                    {
                        entries.Add(outcome.Entry);
                    }

                    if (outcome.Skipped)
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        Interlocked.Increment(ref indexed);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    WriteLog($"[error] {item.Project.Name}/{item.Repository.Name}: {ex.Message}", gate);
                }
            }).ConfigureAwait(false);

        entries.Sort((a, b) =>
        {
            var project = string.Compare(a.Project, b.Project, StringComparison.OrdinalIgnoreCase);
            return project != 0 ? project : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        await WriteCatalogJsonAsync(entries, cancellationToken).ConfigureAwait(false);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        await _embedder.EmbedAsync(entries, cancellationToken).ConfigureAwait(false);

        WriteLog($"[done] indexed={indexed} skipped={skipped} failed={failed} catalog={_options.CatalogJsonPath}", gate);

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
        object gate,
        CancellationToken cancellationToken)
    {
        var wikiFileName = WikiPageWriter.FileName(project.Name, listed.Name);
        var wikiFullPath = Path.Combine(_options.WikiDirectory, wikiFileName);
        var relativeWikiPath = Path.Combine("wiki", wikiFileName).Replace('\\', '/');

        IndexedRepoState? previous;
        lock (gate)
        {
            state.Repos.TryGetValue(listed.Id, out previous);
        }

        var probeBranch = BranchResolver.StripRefsHeads(listed.DefaultBranch);
        if (string.IsNullOrWhiteSpace(probeBranch))
        {
            probeBranch = previous?.Branch;
        }

        string? headSha = null;
        if (!string.IsNullOrWhiteSpace(probeBranch))
        {
            headSha = await _client.GetHeadCommitAsync(project.Name, listed.Id, probeBranch, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(headSha) &&
            ShouldSkip(previous, headSha, wikiFullPath) &&
            WikiPageWriter.TryReadEntry(wikiFullPath, relativeWikiPath, out var existing))
        {
            WriteLog($"[skip] {project.Name}/{listed.Name} @ {headSha}", gate);
            return (existing, Skipped: true);
        }

        var details = listed;
        details.Project ??= project;
        if (string.IsNullOrWhiteSpace(details.Project.Name))
        {
            details.Project.Name = project.Name;
        }

        var branch = probeBranch ?? "";
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(headSha))
        {
            details = await _client.GetRepositoryAsync(project.Name, listed.Id, cancellationToken)
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
                    details.DefaultBranch ?? listed.DefaultBranch,
                    cancellationToken)
                .ConfigureAwait(false);
            branch = resolved?.Name ?? BranchResolver.StripRefsHeads(details.DefaultBranch) ?? "";
            headSha = resolved?.Sha ?? "";
        }

        WriteLog($"[index] {project.Name}/{details.Name} branch={branch} sha={headSha}", gate);

        var snapshot = await FetchSnapshotAsync(project.Name, details, branch, headSha ?? "", cancellationToken)
            .ConfigureAwait(false);
        var entry = RepoInference.Infer(snapshot, _clock.GetUtcNow());
        entry.WikiPath = relativeWikiPath;
        if (string.IsNullOrWhiteSpace(entry.DefaultBranch))
        {
            entry.DefaultBranch = branch;
        }

        var existingPage = File.Exists(wikiFullPath) ? File.ReadAllText(wikiFullPath) : null;
        WikiPageWriter.Write(_options.WikiDirectory, entry, existingPage);

        lock (gate)
        {
            state.Repos[details.Id] = new IndexedRepoState
            {
                HeadSha = headSha ?? "",
                Branch = branch,
                WikiFileName = wikiFileName,
                IndexedAt = entry.LastIndexed,
            };
        }

        return (entry, Skipped: false);
    }

    private void WriteLog(string message, object gate)
    {
        lock (gate)
        {
            _log.WriteLine(message);
        }
    }

    private static bool ShouldSkip(IndexedRepoState? previous, string headSha, string wikiFullPath)
    {
        if (previous is null || !File.Exists(wikiFullPath))
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

        var extraFolder = WorkingSetLimits.MaxExtraShallowFolders > 0
            ? KeyFileSelector.ChooseShallowFolder(root)
            : null;
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

        // Snapshot file bodies stay in memory for this repo only and are discarded after Infer.
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
