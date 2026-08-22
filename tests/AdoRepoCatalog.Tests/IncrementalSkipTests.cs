using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class IncrementalSkipTests
{
    [Fact]
    public async Task Skips_repo_when_head_sha_is_unchanged_and_wiki_page_exists()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());

        var first = await host.Runner.RunAsync();
        Assert.Equal(2, first.IndexedCount);
        Assert.Equal(0, first.SkippedUnchangedCount);

        var wikiBefore = Directory.EnumerateFiles(host.WikiDirectory, "*.md")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);

        host.Fake.ResetCallCounts();
        var second = await host.Runner.RunAsync();

        Assert.Equal(0, second.IndexedCount);
        Assert.Equal(2, second.SkippedUnchangedCount);
        Assert.Equal(0, host.Fake.GetItemContentCalls);
        Assert.Equal(0, host.Fake.ListItemsCalls);
        Assert.Equal(0, host.Fake.GetRepositoryCalls);
        Assert.Equal(2, host.Fake.GetHeadCommitCalls);
        Assert.Equal(2, second.Entries.Count);
        Assert.Contains(second.Entries, entry => entry.Name == "contoso-demo");

        foreach (var (path, before) in wikiBefore)
        {
            Assert.Equal(before, File.ReadAllText(path));
        }
    }

    [Fact]
    public async Task Refreshes_when_head_sha_changes()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        await host.Runner.RunAsync();

        host.Fake.SetHead(FabrikamFixture.ContosoDemoId, "main", "3333333333333333333333333333333333333333");
        host.Fake.ResetCallCounts();

        var second = await host.Runner.RunAsync();

        Assert.Equal(1, second.IndexedCount);
        Assert.Equal(1, second.SkippedUnchangedCount);
        Assert.True(host.Fake.GetItemContentCalls > 0);
        var contoso = Assert.Single(second.Entries, entry => entry.Name == "contoso-demo");
        Assert.Equal("3333333333333333333333333333333333333333", contoso.HeadSha);
    }

    [Fact]
    public async Task Refreshes_when_wiki_page_is_missing_even_if_sha_matches()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        await host.Runner.RunAsync();

        var page = Directory.EnumerateFiles(host.WikiDirectory, "*contoso-demo.md").Single();
        File.Delete(page);
        host.Fake.ResetCallCounts();

        var second = await host.Runner.RunAsync();

        Assert.Equal(1, second.IndexedCount);
        Assert.True(host.Fake.GetItemContentCalls > 0);
        Assert.True(File.Exists(page));
    }
}
