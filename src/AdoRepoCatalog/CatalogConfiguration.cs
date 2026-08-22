using Microsoft.Extensions.Configuration;

namespace AdoRepoCatalog;

public static class CatalogConfiguration
{
    /// <summary>
    /// Precedence (last wins): appsettings.Local.json, user-secrets, environment variables.
    /// </summary>
    public static CatalogOptions Load(string? basePath = null, IEnumerable<KeyValuePair<string, string?>>? extras = null)
    {
        basePath ??= Directory.GetCurrentDirectory();

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(CatalogConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables(prefix: "ADO_");

        if (extras is not null)
        {
            builder.AddInMemoryCollection(extras);
        }

        var config = builder.Build();
        var options = new CatalogOptions();
        config.Bind(options);

        options.Organization = FirstNonEmpty(
            options.Organization,
            config["Organization"],
            config["ORGANIZATION"]) ?? "";

        options.Project = FirstNonEmpty(
            options.Project,
            config["Project"],
            config["PROJECT"]);

        options.PersonalAccessToken = FirstNonEmpty(
            options.PersonalAccessToken,
            config["PersonalAccessToken"],
            config["PAT"],
            config["PERSONALACCESSTOKEN"]);

        options.AccessToken = FirstNonEmpty(
            options.AccessToken,
            config["AccessToken"],
            config["ACCESSTOKEN"]);

        options.OutputDirectory = FirstNonEmpty(
            NullIfDefault(options.OutputDirectory, CatalogOptions.DefaultOutputDirectory),
            config["OutputDirectory"],
            config["OUTPUTDIRECTORY"],
            options.OutputDirectory) ?? CatalogOptions.DefaultOutputDirectory;

        options.StatePath = FirstNonEmpty(
            NullIfDefault(options.StatePath, CatalogOptions.DefaultStatePath),
            config["StatePath"],
            config["STATEPATH"],
            options.StatePath) ?? CatalogOptions.DefaultStatePath;

        options.BaseUrl = FirstNonEmpty(
            NullIfDefault(options.BaseUrl, CatalogOptions.DefaultBaseUrl),
            config["BaseUrl"],
            config["BASEURL"],
            options.BaseUrl) ?? CatalogOptions.DefaultBaseUrl;

        return options;
    }

    private static string? NullIfDefault(string? value, string defaultValue)
        => string.Equals(value, defaultValue, StringComparison.Ordinal) ? null : value;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
