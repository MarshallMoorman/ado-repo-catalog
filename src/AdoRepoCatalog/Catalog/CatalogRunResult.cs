namespace AdoRepoCatalog.Catalog;

public sealed class CatalogRunResult
{
    public required string CatalogJsonPath { get; init; }

    public required string WikiDirectory { get; init; }

    public IReadOnlyList<CatalogEntry> Entries { get; init; } = [];

    public int IndexedCount { get; init; }

    public int SkippedUnchangedCount { get; init; }

    public int FailedCount { get; init; }
}
