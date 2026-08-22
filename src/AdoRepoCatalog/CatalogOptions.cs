namespace AdoRepoCatalog;

/// <summary>
/// Runtime options. Load from environment variables, user-secrets, then
/// gitignored appsettings.Local.json. Never commit tokens.
/// </summary>
public sealed class CatalogOptions
{
    public const string DefaultBaseUrl = "https://dev.azure.com";
    public const string DefaultApiVersion = "7.1";
    public const string DefaultOutputDirectory = "out";
    public const string DefaultStatePath = ".ado-catalog/state.json";

    public string Organization { get; set; } = "";

    /// <summary>When empty, every well-formed project in the organization is scanned.</summary>
    public string? Project { get; set; }

    public string? PersonalAccessToken { get; set; }

    public string? AccessToken { get; set; }

    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public string StatePath { get; set; } = DefaultStatePath;

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string ApiVersion { get; set; } = DefaultApiVersion;

    public string WikiDirectory => Path.Combine(OutputDirectory, "wiki");

    public string CatalogJsonPath => Path.Combine(OutputDirectory, "catalog.json");

    public bool HasPersonalAccessToken => !string.IsNullOrWhiteSpace(PersonalAccessToken);

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    public bool TryValidate(out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(Organization))
        {
            list.Add("Organization is required (environment, user-secrets, or appsettings.Local.json).");
        }

        if (!HasPersonalAccessToken && !HasAccessToken)
        {
            list.Add("PersonalAccessToken or AccessToken is required. Do not commit the value.");
        }

        errors = list;
        return list.Count == 0;
    }
}
