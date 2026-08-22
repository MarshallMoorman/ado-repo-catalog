using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Catalog;

namespace AdoRepoCatalog.Tests;

public sealed class BranchFallbackTests
{
    [Fact]
    public async Task Uses_develop_when_default_branch_is_missing()
    {
        using var host = new CatalogTestHost(FabrikamFixture.CreateClient());

        var result = await host.Runner.RunAsync();
        var web = Assert.Single(result.Entries, entry => entry.Name == "fabrikam-web");

        Assert.Equal("develop", web.DefaultBranch);
        Assert.Equal(FabrikamFixture.FabrikamWebHead, web.HeadSha);

        var page = File.ReadAllText(Path.Combine(host.WikiDirectory, Path.GetFileName(web.WikiPath)));
        Assert.Contains("defaultBranch: \"develop\"", page);
    }

    [Fact]
    public async Task Skips_missing_default_branch_then_tries_fallback_order()
    {
        var client = new FakeAzureDevOpsClient();
        var project = new AdoProject
        {
            Id = FabrikamFixture.ProjectId,
            Name = FabrikamFixture.ProjectName,
            State = "wellFormed",
        };
        client.Projects.Add(project);
        client.Repositories.Add(new AdoRepository
        {
            Id = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            Name = "contoso-demo",
            DefaultBranch = "refs/heads/does-not-exist",
            RemoteUrl = "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
            Project = project,
        });
        client.SetHead("cccccccc-cccc-cccc-cccc-cccccccccccc", "dev", "4444444444444444444444444444444444444444");
        client.AddItems("cccccccc-cccc-cccc-cccc-cccccccccccc", "dev", "/",
            FabrikamFixture.File("/README.md"));
        client.AddFile("cccccccc-cccc-cccc-cccc-cccccccccccc", "dev", "/README.md",
            "This demo service stores fictional catalog cards for local routing tests.");

        using var host = new CatalogTestHost(client);
        var result = await host.Runner.RunAsync();
        var entry = Assert.Single(result.Entries);

        Assert.Equal("dev", entry.DefaultBranch);
        Assert.Equal("4444444444444444444444444444444444444444", entry.HeadSha);
        Assert.True(client.GetHeadCommitCalls >= 3);
    }

    [Fact]
    public void Candidate_order_puts_default_branch_first_then_documented_fallbacks()
    {
        var candidates = BranchResolver.Candidates("refs/heads/release");
        Assert.Equal("release", candidates[0]);
        Assert.Equal(
            new[] { "develop", "dev", "Develop", "Dev", "main", "master", "Main", "Master" },
            BranchResolver.FallbackOrder);
        Assert.Equal(BranchResolver.FallbackOrder, candidates.Skip(1));
    }
}
