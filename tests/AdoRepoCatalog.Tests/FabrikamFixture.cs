using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Tests;

/// <summary>
/// Official-style fictional Azure DevOps organization used only in tests.
/// Names match Microsoft Learn samples (fabrikam) plus contoso-demo.
/// </summary>
public static class FabrikamFixture
{
    public const string Organization = "fabrikam";
    public const string ProjectName = "Fabrikam-Fiber-Git";
    public const string ProjectId = "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c";

    public const string ContosoDemoId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string FabrikamWebId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    public const string ContosoHead = "1111111111111111111111111111111111111111";
    public const string FabrikamWebHead = "2222222222222222222222222222222222222222";

    public static FakeAzureDevOpsClient CreateClient()
    {
        var project = new AdoProject
        {
            Id = ProjectId,
            Name = ProjectName,
            Description = "Git projects",
            State = "wellFormed",
            Url = $"https://dev.azure.com/{Organization}/_apis/projects/{ProjectId}",
        };

        var contoso = new AdoRepository
        {
            Id = ContosoDemoId,
            Name = "contoso-demo",
            DefaultBranch = "refs/heads/main",
            RemoteUrl = $"https://dev.azure.com/{Organization}/{ProjectName}/_git/contoso-demo",
            Project = project,
        };

        var web = new AdoRepository
        {
            Id = FabrikamWebId,
            Name = "fabrikam-web",
            DefaultBranch = null,
            RemoteUrl = $"https://dev.azure.com/{Organization}/{ProjectName}/_git/fabrikam-web",
            Project = project,
        };

        var fake = new FakeAzureDevOpsClient();
        fake.Projects.Add(project);
        fake.Repositories.Add(contoso);
        fake.Repositories.Add(web);

        fake.SetHead(ContosoDemoId, "main", ContosoHead);
        fake.AddItems(ContosoDemoId, "main", "/",
            Folder("/"),
            File("/README.md"),
            File("/ContosoDemo.sln"),
            File("/Dockerfile"),
            File("/azure-pipelines.yml"),
            File("/appsettings.json"),
            Folder("/src"));
        fake.AddItems(ContosoDemoId, "main", "/src",
            Folder("/src"),
            File("/src/ContosoDemo.Api.csproj"),
            File("/src/Program.cs"),
            File("/src/OrdersController.cs"));
        fake.AddFile(ContosoDemoId, "main", "/README.md", "   \n");
        fake.AddFile(ContosoDemoId, "main", "/ContosoDemo.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        fake.AddFile(ContosoDemoId, "main", "/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:10.0\nEXPOSE 8080\n");
        fake.AddFile(ContosoDemoId, "main", "/azure-pipelines.yml", "trigger:\n- main\n");
        fake.AddFile(ContosoDemoId, "main", "/appsettings.json", """{"ConnectionStrings":{"Catalog":"(local-fixture)"}}""");
        fake.AddFile(ContosoDemoId, "main", "/src/ContosoDemo.Api.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Description>Demo orders API</Description>
              </PropertyGroup>
            </Project>
            """);
        fake.AddFile(ContosoDemoId, "main", "/src/Program.cs",
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
        fake.AddFile(ContosoDemoId, "main", "/src/OrdersController.cs",
            """
            using Microsoft.AspNetCore.Mvc;
            namespace ContosoDemo.Api;
            public sealed class OrdersController : ControllerBase { }
            """);

        fake.SetHead(FabrikamWebId, "develop", FabrikamWebHead);
        fake.AddItems(FabrikamWebId, "develop", "/",
            Folder("/"),
            File("/README.md"),
            File("/package.json"));
        fake.AddFile(FabrikamWebId, "develop", "/README.md",
            """
            # fabrikam-web

            Contoso storefront UI for browsing the fictional catalog and placing demo orders.
            """);
        fake.AddFile(FabrikamWebId, "develop", "/package.json",
            """
            {
              "name": "fabrikam-web",
              "description": "Fictional storefront for the contoso-demo catalog",
              "dependencies": {
                "react": "18.3.1",
                "typescript": "5.6.0"
              }
            }
            """);

        return fake;
    }

    public static AdoItem File(string path) => new()
    {
        Path = path,
        GitObjectType = "blob",
        IsFolder = false,
        ObjectId = "blob-" + path.GetHashCode(StringComparison.Ordinal),
    };

    public static AdoItem Folder(string path) => new()
    {
        Path = path,
        GitObjectType = "tree",
        IsFolder = true,
        ObjectId = "tree-" + path.GetHashCode(StringComparison.Ordinal),
    };
}
