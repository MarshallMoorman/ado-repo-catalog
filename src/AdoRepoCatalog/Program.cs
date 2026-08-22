using AdoRepoCatalog;
using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Catalog;
using AdoRepoCatalog.Embedding;

if (args.Any(arg => arg is "-h" or "--help" or "-?"))
{
    PrintHelp();
    return 0;
}

var extras = new List<KeyValuePair<string, string?>>();
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o")
    {
        extras.Add(new("OutputDirectory", args[i + 1]));
    }
}

var options = CatalogConfiguration.Load(extras: extras);
if (!options.TryValidate(out var errors))
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }

    Console.Error.WriteLine();
    PrintHelp();
    return 1;
}

using var http = AzureDevOpsClient.CreateHttpClient(options);
var client = new AzureDevOpsClient(http, options);
var stateStore = new FileIndexStateStore(options.StatePath);
var runner = new CatalogRunner(options, client, stateStore, NoOpCatalogEmbedder.Instance);

try
{
    var result = await runner.RunAsync().ConfigureAwait(false);
    Console.WriteLine($"Wiki pages: {result.WikiDirectory}");
    Console.WriteLine($"catalog.json: {result.CatalogJsonPath}");
    return result.FailedCount == 0 ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        AdoRepoCatalog — index Azure DevOps git repos into wiki pages + catalog.json.

        Configuration (first non-empty wins after env > user-secrets > appsettings.Local.json):
          Organization              ADO organization name (not a URL)
          Project                   Optional. Empty = scan every project
          PersonalAccessToken       PAT used as HTTP Basic password (empty username)
          AccessToken               Optional Bearer token instead of a PAT
          OutputDirectory           Default: ./out  (gitignored)
          StatePath                 Default: ./.ado-catalog/state.json (gitignored)

        Environment aliases: ADO_ORGANIZATION, ADO_PROJECT, ADO_PAT / ADO_PERSONALACCESSTOKEN,
          ADO_ACCESSTOKEN, ADO_OUTPUTDIRECTORY, ADO_STATEPATH, ADO_BASEURL

        Required PAT scopes:
          Code (Read)
          Project (Read)

        Laptop run:
          cd src/AdoRepoCatalog
          dotnet user-secrets set Organization <org>
          dotnet user-secrets set PersonalAccessToken <pat>
          dotnet run --project . -- --output ../../out

        Never commit PATs, organization names from work, or appsettings.Local.json.
        This tool never clones git history. v1 writes wiki + catalog.json only.
        """);
}
