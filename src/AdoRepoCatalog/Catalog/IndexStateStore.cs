using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdoRepoCatalog.Catalog;

public sealed class IndexState
{
    [JsonPropertyName("repos")]
    public Dictionary<string, IndexedRepoState> Repos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class IndexedRepoState
{
    [JsonPropertyName("headSha")]
    public string HeadSha { get; set; } = "";

    [JsonPropertyName("wikiFileName")]
    public string WikiFileName { get; set; } = "";

    [JsonPropertyName("indexedAt")]
    public DateTimeOffset? IndexedAt { get; set; }
}

public interface IIndexStateStore
{
    Task<IndexState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IndexState state, CancellationToken cancellationToken = default);
}

public sealed class FileIndexStateStore : IIndexStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public FileIndexStateStore(string path)
    {
        _path = path;
    }

    public async Task<IndexState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new IndexState();
        }

        await using var stream = File.OpenRead(_path);
        var state = await JsonSerializer.DeserializeAsync<IndexState>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return state ?? new IndexState();
    }

    public async Task SaveAsync(IndexState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
