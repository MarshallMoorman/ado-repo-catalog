using System.Text.Json;
using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class FixtureCatalogTests
{
    [Fact]
    public async Task Fixture_org_writes_catalog_json_and_one_wiki_page_per_repo()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());

        var result = await host.Runner.RunAsync();

        Assert.Equal(2, result.IndexedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(host.CatalogJsonPath));
        Assert.True(Directory.Exists(host.WikiDirectory));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(host.CatalogJsonPath));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());

        var names = document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("contoso-demo", names);
        Assert.Contains("fabrikam-web", names);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("project").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("remoteUrl").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("defaultBranch").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("headSha").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("whenToWriteHere").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("wikiPath").GetString()));
            Assert.True(item.GetProperty("confidence").GetDouble() is >= 0 and <= 1);
            var wikiPath = Path.Combine(host.Options.OutputDirectory, item.GetProperty("wikiPath").GetString()!);
            Assert.True(File.Exists(wikiPath), wikiPath);
        }

        var contosoPage = File.ReadAllText(
            Directory.EnumerateFiles(host.WikiDirectory, "*contoso-demo.md").Single());
        Assert.Contains("## Purpose", contosoPage);
        Assert.Contains("## Stack", contosoPage);
        Assert.Contains("## Services / functionality", contosoPage);
        Assert.Contains("## Routing guidance", contosoPage);
        Assert.Contains("## Generated from files", contosoPage);
        Assert.Contains("## Human override", contosoPage);
        Assert.Contains("`/src/OrdersController.cs`", contosoPage);
        Assert.Contains("aspnetcore", contosoPage);
        Assert.Contains("orders", contosoPage);
        Assert.DoesNotContain("dev.azure.com/contoso-corp", contosoPage);

        var webPage = File.ReadAllText(
            Directory.EnumerateFiles(host.WikiDirectory, "*fabrikam-web.md").Single());
        Assert.Contains("Contoso storefront UI", webPage);
        Assert.Contains("react", webPage);
        Assert.Contains("typescript", webPage);
        Assert.DoesNotContain("WebApplication.CreateBuilder", contosoPage);
        Assert.DoesNotContain("ControllerBase", contosoPage);
    }

    [Fact]
    public async Task Empty_readme_still_infers_stack_from_project_files()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        var result = await host.Runner.RunAsync();
        var contoso = Assert.Single(result.Entries, entry => entry.Name == "contoso-demo");

        Assert.Contains("csharp", contoso.Languages);
        Assert.Contains("aspnetcore", contoso.Frameworks);
        Assert.Contains("orders", contoso.Services);
        Assert.True(contoso.Confidence >= 0.4);
        Assert.False(contoso.LowConfidence);
        Assert.Contains("HTTP endpoints", contoso.WhenToWriteHere);
    }
}
