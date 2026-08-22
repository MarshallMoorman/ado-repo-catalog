using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class OverridePreservationTests
{
    [Fact]
    public async Task Preserves_override_markers_verbatim_on_refresh()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        var first = await host.Runner.RunAsync();
        var entry = Assert.Single(first.Entries, e => e.Name == "contoso-demo");
        var pagePath = Path.Combine(host.WikiDirectory, Path.GetFileName(entry.WikiPath));

        var generated = File.ReadAllText(pagePath);
        var humanNote = """
            <!-- OVERRIDE:START -->
            Route billing changes to this repo only after the demo ledger lands.
            Keep the fictional SKU list in sync with fabrikam-web.
            <!-- OVERRIDE:END -->
            """;
        var edited = ReplaceOverride(generated, humanNote);
        File.WriteAllText(pagePath, edited);

        host.Client.SetHead(FabrikamFixture.ContosoDemoId, "main", "5555555555555555555555555555555555555555");
        host.Client.AddFile(FabrikamFixture.ContosoDemoId, "main", "/README.md",
            "Updated fictional purpose: demo checkout API for the contoso catalog.");

        var second = await host.Runner.RunAsync();
        var refreshed = File.ReadAllText(Path.Combine(host.WikiDirectory, Path.GetFileName(entry.WikiPath)));

        Assert.Contains("Updated fictional purpose: demo checkout API for the contoso catalog.", refreshed);
        Assert.Contains("5555555555555555555555555555555555555555", refreshed);
        Assert.Contains(humanNote.Replace("\r\n", "\n", StringComparison.Ordinal), Normalize(refreshed));
        Assert.DoesNotContain("<!-- Add durable notes below.", refreshed);
        Assert.Equal(1, second.IndexedCount);
    }

    [Fact]
    public async Task Preserves_human_override_heading_when_markers_are_absent()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        var first = await host.Runner.RunAsync();
        var entry = Assert.Single(first.Entries, e => e.Name == "fabrikam-web");
        var pagePath = Path.Combine(host.WikiDirectory, Path.GetFileName(entry.WikiPath));

        var generated = File.ReadAllText(pagePath);
        var headingIndex = generated.IndexOf(WikiPageWriter.OverrideHeading, StringComparison.Ordinal);
        Assert.True(headingIndex >= 0);
        var withHeading = generated[..headingIndex] + """
            ## Human override

            Own the storefront copy. Do not put API contracts here.
            """;
        File.WriteAllText(pagePath, withHeading);

        host.Client.SetHead(FabrikamFixture.FabrikamWebId, "develop", "6666666666666666666666666666666666666666");
        var second = await host.Runner.RunAsync();
        var refreshed = File.ReadAllText(pagePath);

        Assert.Contains("## Human override", refreshed);
        Assert.Contains("Own the storefront copy. Do not put API contracts here.", refreshed);
        Assert.Contains("6666666666666666666666666666666666666666", refreshed);
        Assert.Equal(1, second.IndexedCount);
    }

    private static string ReplaceOverride(string page, string replacement)
    {
        var start = page.IndexOf(WikiPageWriter.OverrideStart, StringComparison.Ordinal);
        var end = page.IndexOf(WikiPageWriter.OverrideEnd, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return page[..start] + replacement + page[(end + WikiPageWriter.OverrideEnd.Length)..];
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
