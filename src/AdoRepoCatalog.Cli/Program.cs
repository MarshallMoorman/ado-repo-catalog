using System.Reflection;
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

var options = CatalogConfiguration.Load(
    userSecretsAssembly: Assembly.GetExecutingAssembly(),
    extras: extras);
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

        This tool never clones product repos (no git clone, no sparse checkout) and never
        writes back to those remotes. The only outputs are local wiki pages and catalog.json.

        Configuration (later sources win: appsettings.Local.json, user-secrets, environment):
          Organization              ADO organization name (not a URL)
          Project                   Optional. Empty = scan every project
          PersonalAccessToken       PAT used as HTTP Basic password (empty username)
          AccessToken               Optional Bearer token instead of a PAT
          OutputDirectory           Default: ./out  (gitignored)
          StatePath                 Default: ./.ado-catalog/state.json (gitignored)
          MaxConcurrency            Repos in flight at once. Default 3, clamped to 2–4

        Environment aliases: ADO_ORGANIZATION, ADO_PROJECT, ADO_PAT / ADO_PERSONALACCESSTOKEN,
          ADO_ACCESSTOKEN, ADO_OUTPUTDIRECTORY, ADO_STATEPATH, ADO_BASEURL, ADO_MAXCONCURRENCY

        Required PAT scopes:
          Code (Read)
          Project (Read)

        Use one configured credential. Do not add extra PATs to dodge rate limits.
        ADO TSTU budget is about 200 per user per 5 minutes; incremental HEAD SHA skip
        keeps later laptop runs cheap. The client honors Retry-After, X-RateLimit-Delay,
        and X-RateLimit-Remaining (including delay hints on HTTP 200).

        Laptop run:
          cd src/AdoRepoCatalog.Cli
          dotnet user-secrets set Organization "<your-ado-org>"
          dotnet user-secrets set PersonalAccessToken "<your-pat>"
          dotnet run --project . -- --output ../../out

        Never commit PATs, work organization names, or appsettings.Local.json.
        """);
}
