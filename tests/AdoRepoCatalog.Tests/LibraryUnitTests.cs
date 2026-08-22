using AdoRepoCatalog;
using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Catalog;
using AdoRepoCatalog.Infrastructure;

namespace AdoRepoCatalog.Tests;

public sealed class LibraryUnitTests
{
    [Fact]
    public void Slug_is_stable_and_safe()
    {
        Assert.Equal("fabrikam-fiber-git-contoso-demo", Slug.From("Fabrikam-Fiber-Git", "contoso-demo"));
        Assert.Equal("unnamed", Slug.From("   "));
        Assert.Equal("a-b", Slug.From("A  B!!"));
    }

    [Fact]
    public void Key_file_selector_matches_documented_names_only()
    {
        Assert.True(KeyFileSelector.IsKeyFile("/README.md"));
        Assert.True(KeyFileSelector.IsKeyFile("/src/OrdersController.cs"));
        Assert.True(KeyFileSelector.IsKeyFile("/azure-pipelines.yml"));
        Assert.False(KeyFileSelector.IsKeyFile("/notes.txt"));
        Assert.False(KeyFileSelector.IsKeyFile("/src/util.ts"));
        Assert.Equal("src", KeyFileSelector.ChooseShallowFolder(
        [
            FabrikamFixture.Folder("/src"),
            FabrikamFixture.Folder("/docs"),
        ]));
    }

    [Fact]
    public void Wiki_front_matter_round_trips()
    {
        var entry = new CatalogEntry
        {
            Name = "contoso-demo",
            Project = "Fabrikam-Fiber-Git",
            RemoteUrl = "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
            DefaultBranch = "main",
            HeadSha = "abc",
            Languages = ["csharp"],
            Frameworks = ["aspnetcore"],
            Services = ["orders"],
            Confidence = 0.7,
            LowConfidence = false,
            LastIndexed = new DateTimeOffset(2026, 8, 22, 1, 0, 0, TimeSpan.Zero),
            WhenToWriteHere = "Add API changes here.",
            Purpose = "Demo",
            StackSummary = "csharp",
            ServicesSummary = "orders",
            WikiPath = "wiki/fabrikam-fiber-git-contoso-demo.md",
        };

        var page = WikiPageWriter.Compose(entry, existingPage: null);
        Assert.True(WikiPageWriter.TryParseFrontMatter(page, out var parsed));
        Assert.Equal("contoso-demo", parsed.Name);
        Assert.Equal("main", parsed.DefaultBranch);
        Assert.Contains("csharp", parsed.Languages);
        Assert.Contains(WikiPageWriter.OverrideStart, page);
    }

    [Fact]
    public void Configuration_reads_extras_and_max_concurrency()
    {
        var options = CatalogConfiguration.Load(
            extras:
            [
                new("Organization", "fabrikam"),
                new("PersonalAccessToken", "not-a-real-pat"),
                new("MaxConcurrency", "4"),
                new("OutputDirectory", "/tmp/ado-out"),
            ]);

        Assert.Equal("fabrikam", options.Organization);
        Assert.Equal(4, options.MaxConcurrency);
        Assert.Equal("/tmp/ado-out", options.OutputDirectory);
        Assert.True(options.TryValidate(out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void Configuration_requires_org_and_token()
    {
        var options = new CatalogOptions();
        Assert.False(options.TryValidate(out var errors));
        Assert.Contains(errors, error => error.Contains("Organization", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PersonalAccessToken", StringComparison.Ordinal));
    }

    [Fact]
    public void Inference_reads_package_json_go_and_python_signals()
    {
        var repo = new AdoRepository
        {
            Name = "contoso-demo",
            Project = new AdoProject { Name = "Fabrikam-Fiber-Git" },
            RemoteUrl = "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
        };

        var node = RepoInference.Infer(new RepoSnapshot
        {
            Repository = repo,
            Branch = "main",
            HeadSha = "1",
            TreeEntries = [],
            Files = new Dictionary<string, string>
            {
                ["/package.json"] = """{ "description": "Tiny fictional storefront", "dependencies": { "next": "14.0.0", "react": "18.0.0" } }""",
            },
        }, DateTimeOffset.UtcNow);
        Assert.Contains("javascript", node.Languages);
        Assert.Contains("nextjs", node.Frameworks);

        var go = RepoInference.Infer(new RepoSnapshot
        {
            Repository = repo,
            Branch = "main",
            HeadSha = "1",
            TreeEntries = [],
            Files = new Dictionary<string, string>
            {
                ["/go.mod"] = "module example.com/contoso-demo\n",
            },
        }, DateTimeOffset.UtcNow);
        Assert.Contains("go", go.Languages);
        Assert.Contains("contoso-demo", go.Services);

        var py = RepoInference.Infer(new RepoSnapshot
        {
            Repository = repo,
            Branch = "main",
            HeadSha = "1",
            TreeEntries = [],
            Files = new Dictionary<string, string>
            {
                ["/pyproject.toml"] = "[project]\nname = \"contoso-demo\"\ndependencies = [\"fastapi\"]\n",
            },
        }, DateTimeOffset.UtcNow);
        Assert.Contains("python", py.Languages);
        Assert.Contains("fastapi", py.Frameworks);
    }

    [Fact]
    public void Inference_uses_agents_and_appsettings_when_readme_is_empty()
    {
        var repo = new AdoRepository
        {
            Name = "contoso-demo",
            Project = new AdoProject { Name = "Fabrikam-Fiber-Git" },
            RemoteUrl = "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
        };

        var entry = RepoInference.Infer(new RepoSnapshot
        {
            Repository = repo,
            Branch = "main",
            HeadSha = "1",
            TreeEntries = [FabrikamFixture.Folder("/src")],
            Files = new Dictionary<string, string>
            {
                ["/README.md"] = "\n",
                ["/AGENTS.md"] = "Route fictional catalog work to this repository when the storefront copy changes.",
                ["/appsettings.json"] = """{"ServiceBus":{},"Redis":{}}""",
                ["/worker.csproj"] = """<Project Sdk="Microsoft.NET.Sdk.Worker"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""",
            },
        }, DateTimeOffset.UtcNow);

        Assert.Contains("storefront copy", entry.Purpose);
        Assert.Contains("dotnet-worker", entry.Frameworks);
        Assert.Contains("servicebus", entry.Services);
        Assert.Contains("cache", entry.Services);
    }

    [Fact]
    public void Score_without_signals_stays_low()
    {
        Assert.Equal(0.08, RepoInference.Score([]));
        Assert.True(RepoInference.Score([]) < RepoInference.LowConfidenceThreshold);
    }

    [Fact]
    public void Key_file_selector_recognizes_shallow_scan_folders()
    {
        Assert.True(KeyFileSelector.IsShallowScanFolder("src"));
        Assert.True(KeyFileSelector.IsShallowScanFolder("API"));
        Assert.False(KeyFileSelector.IsShallowScanFolder("docs"));
    }

    [Fact]
    public async Task System_delay_completes_immediately_for_zero()
    {
        Assert.NotNull(SystemAsyncDelay.Instance);
        await SystemAsyncDelay.Instance.Delay(TimeSpan.Zero);
    }

    [Fact]
    public void Branch_resolver_strips_refs_heads()
    {
        Assert.Equal("develop", BranchResolver.StripRefsHeads("refs/heads/develop"));
        Assert.Null(BranchResolver.StripRefsHeads(" "));
        Assert.Equal("develop", BranchResolver.Candidates("refs/heads/develop")[0]);
        Assert.Equal("dev", BranchResolver.Candidates(null)[1]);
    }
}
