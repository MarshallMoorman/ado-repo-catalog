using AdoRepoCatalog;
using AdoRepoCatalog.Catalog;
using AdoRepoCatalog.Embedding;

namespace AdoRepoCatalog.Tests;

internal sealed class CatalogTestHost : IDisposable
{
    public CatalogTestHost(FakeAzureDevOpsClient client)
    {
        Client = client;
        Root = Directory.CreateTempSubdirectory("ado-catalog-test-");
        Options = new CatalogOptions
        {
            Organization = FabrikamFixture.Organization,
            OutputDirectory = Path.Combine(Root.FullName, "out"),
            StatePath = Path.Combine(Root.FullName, ".ado-catalog", "state.json"),
        };
        Clock = new FixedUtcClock(new DateTimeOffset(2026, 8, 22, 1, 0, 0, TimeSpan.Zero));
        StateStore = new FileIndexStateStore(Options.StatePath);
        Log = new StringWriter();
        Runner = new CatalogRunner(Options, Client, StateStore, NoOpCatalogEmbedder.Instance, Clock, Log);
    }

    public FakeAzureDevOpsClient Client { get; }

    public DirectoryInfo Root { get; }

    public CatalogOptions Options { get; }

    public FileIndexStateStore StateStore { get; }

    public CatalogRunner Runner { get; }

    public FixedUtcClock Clock { get; }

    public StringWriter Log { get; }

    public string WikiDirectory => Options.WikiDirectory;

    public string CatalogJsonPath => Options.CatalogJsonPath;

    public void Dispose()
    {
        try
        {
            if (Root.Exists)
            {
                Root.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
            // temp cleanup is best-effort
        }
    }
}

internal sealed class FixedUtcClock : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedUtcClock(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
