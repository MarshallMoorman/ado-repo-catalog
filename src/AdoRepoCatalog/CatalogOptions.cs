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
    public const int DefaultMaxConcurrency = 3;
    public const int MinConcurrency = 2;
    public const int MaxAllowedConcurrency = 4;

    public string Organization { get; set; } = "";

    /// <summary>When empty, every well-formed project in the organization is scanned.</summary>
    public string? Project { get; set; }

    public string? PersonalAccessToken { get; set; }

    public string? AccessToken { get; set; }

    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public string StatePath { get; set; } = DefaultStatePath;

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string ApiVersion { get; set; } = DefaultApiVersion;

    /// <summary>Repos processed at once. Clamped to 2–4. Default 3.</summary>
    public int MaxConcurrency { get; set; } = DefaultMaxConcurrency;

    /// <summary>HTTP retries after 429. Default 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>0 disables jitter (useful in tests). Default 0.25.</summary>
    public double RetryJitterRatio { get; set; } = 0.25;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public int EffectiveMaxConcurrency
    {
        get
        {
            var value = MaxConcurrency <= 0 ? DefaultMaxConcurrency : MaxConcurrency;
            return Math.Clamp(value, MinConcurrency, MaxAllowedConcurrency);
        }
    }

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
