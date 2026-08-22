using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Embedding;

/// <summary>
/// v1 stub. Later implementations may embed generated wiki pages (never source)
/// into Qdrant or Azure AI Search. Must not block catalog generation.
/// </summary>
public interface ICatalogEmbedder
{
    Task EmbedAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken = default);
}
