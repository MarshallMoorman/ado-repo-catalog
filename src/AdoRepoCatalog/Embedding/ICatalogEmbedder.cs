using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Embedding;

/// <summary>
/// Optional embedder for generated wiki pages (never source). The default
/// implementation is a no-op so catalog generation does not depend on a vector store.
/// </summary>
public interface ICatalogEmbedder
{
    Task EmbedAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken = default);
}
