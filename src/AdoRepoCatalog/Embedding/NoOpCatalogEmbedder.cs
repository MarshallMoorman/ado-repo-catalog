using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Embedding;

public sealed class NoOpCatalogEmbedder : ICatalogEmbedder
{
    public static NoOpCatalogEmbedder Instance { get; } = new();

    public Task EmbedAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
