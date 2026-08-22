using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class RestOnlyAndPublishTests
{
    [Fact]
    public void Library_never_shells_git_clone_or_sparse_checkout()
    {
        var root = FindRepoRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "src", "AdoRepoCatalog"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("git clone", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sparse-checkout", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Process.Start", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            typeof(IAzureDevOpsClient).GetMethods(),
            method => method.Name.Contains("Clone", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("Checkout", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("Push", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("PullRequest", StringComparison.OrdinalIgnoreCase));

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("PostAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PutAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PatchAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DeleteAsync", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Fixture_repos_are_tiny_and_fictional()
    {
        var client = FabrikamFixture.CreateClient();
        FabrikamFixture.AddEmptyNotesRepo(client);

        Assert.Equal("fabrikam", FabrikamFixture.Organization);
        Assert.All(client.Repositories, repo =>
            Assert.StartsWith("https://dev.azure.com/fabrikam/", repo.RemoteUrl, StringComparison.OrdinalIgnoreCase));

        foreach (var files in client.ContentByRepoBranchPath.Values.SelectMany(branches => branches.Values))
        {
            Assert.InRange(files.Count, 1, 10);
            Assert.All(files.Values, content => Assert.True(content.Length <= 800, content));
        }
    }

    [Fact]
    public async Task Low_confidence_still_publishes_a_flagged_page()
    {
        var client = FabrikamFixture.CreateClient();
        FabrikamFixture.AddEmptyNotesRepo(client);
        using var host = new CatalogTestHost(client);

        var result = await host.Runner.RunAsync();
        var notes = Assert.Single(result.Entries, entry => entry.Name == "fabrikam-notes");

        Assert.True(notes.LowConfidence);
        Assert.True(notes.Confidence < RepoInference.LowConfidenceThreshold);
        Assert.True(File.Exists(Path.Combine(host.WikiDirectory, Path.GetFileName(notes.WikiPath))));
        var page = File.ReadAllText(Path.Combine(host.WikiDirectory, Path.GetFileName(notes.WikiPath)));
        Assert.Contains("lowConfidence: true", page);
        Assert.Contains("Low-confidence catalog entry", page);
        Assert.Contains("## Purpose", page);
    }

    [Fact]
    public async Task Wiki_and_state_keep_summaries_not_source_blobs()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());
        await host.Runner.RunAsync();

        foreach (var page in Directory.EnumerateFiles(host.WikiDirectory, "*.md"))
        {
            var text = File.ReadAllText(page);
            Assert.DoesNotContain("WebApplication.CreateBuilder", text);
            Assert.DoesNotContain("ControllerBase", text);
            Assert.DoesNotContain("\"react\": \"18.3.1\"", text);
        }

        var state = await File.ReadAllTextAsync(host.Options.StatePath);
        Assert.DoesNotContain("WebApplication", state);
        Assert.DoesNotContain("FROM mcr.microsoft.com", state);
        Assert.Contains("headSha", state);
        Assert.DoesNotContain("content", state, StringComparison.OrdinalIgnoreCase);

        var catalog = await File.ReadAllTextAsync(host.CatalogJsonPath);
        Assert.DoesNotContain("WebApplication.CreateBuilder", catalog);
        Assert.DoesNotContain("var builder", catalog);
        using var document = System.Text.Json.JsonDocument.Parse(catalog);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Assert.False(item.TryGetProperty("generatedFromFiles", out _));
            Assert.False(item.TryGetProperty("purpose", out _));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AdoRepoCatalog.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the solution root from the test output directory.");
    }
}
