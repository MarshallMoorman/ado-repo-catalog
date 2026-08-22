using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdoRepoCatalog.Infrastructure;

namespace AdoRepoCatalog.AzureDevOps;

/// <summary>
/// Azure DevOps REST client for official 7.1 shapes on dev.azure.com.
/// Auth is PAT as HTTP Basic (empty username) or Bearer.
/// </summary>
public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    public const string ContinuationTokenHeader = "x-ms-continuationtoken";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly CatalogOptions _options;

    public AzureDevOpsClient(HttpClient http, CatalogOptions options)
    {
        _http = http;
        _options = options;
        Configure(_http, options);
    }

    public static HttpClient CreateHttpClient(
        CatalogOptions options,
        IAsyncDelay? delay = null,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? innerHandler = null)
    {
        var throttle = new AdoThrottlingHandler(options, delay, timeProvider, innerHandler ?? new HttpClientHandler());
        var http = new HttpClient(throttle, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        Configure(http, options);
        return http;
    }

    public static void Configure(HttpClient http, CatalogOptions options)
    {
        var org = Uri.EscapeDataString(options.Organization.Trim());
        var baseUrl = options.BaseUrl.TrimEnd('/');
        http.BaseAddress = new Uri($"{baseUrl}/{org}/");
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AdoRepoCatalog/1.0");

        if (options.HasPersonalAccessToken)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{options.PersonalAccessToken}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
        else if (options.HasAccessToken)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        }
    }

    public Task<IReadOnlyList<AdoProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
        => GetPagedAsync<AdoProject>($"_apis/projects?$top=100&api-version={_options.ApiVersion}", cancellationToken);

    public Task<IReadOnlyList<AdoRepository>> ListRepositoriesAsync(
        string project,
        CancellationToken cancellationToken = default)
        => GetPagedAsync<AdoRepository>(
            $"{Encode(project)}/_apis/git/repositories?api-version={_options.ApiVersion}",
            cancellationToken);

    public async Task<AdoRepository> GetRepositoryAsync(
        string project,
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        var path = $"{Encode(project)}/_apis/git/repositories/{Encode(repositoryId)}?api-version={_options.ApiVersion}";
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var repo = JsonSerializer.Deserialize<AdoRepository>(body, JsonOptions);
        if (repo is null || string.IsNullOrWhiteSpace(repo.Id))
        {
            throw new InvalidOperationException($"Repository payload for '{repositoryId}' was empty.");
        }

        return repo;
    }

    public async Task<string?> GetHeadCommitAsync(
        string project,
        string repositoryId,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var path =
            $"{Encode(project)}/_apis/git/repositories/{Encode(repositoryId)}/commits" +
            $"?searchCriteria.$top=1&searchCriteria.itemVersion.version={Uri.EscapeDataString(branch)}" +
            $"&api-version={_options.ApiVersion}";

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        await EnsureSuccessAsync(response, path).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var collection = JsonSerializer.Deserialize<AdoCollection<AdoCommit>>(body, JsonOptions);
        var commit = collection?.Value.FirstOrDefault();
        return string.IsNullOrWhiteSpace(commit?.CommitId) ? null : commit.CommitId;
    }

    public async Task<IReadOnlyList<AdoItem>> ListItemsAsync(
        string project,
        string repositoryId,
        string scopePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var scope = string.IsNullOrWhiteSpace(scopePath) ? "/" : scopePath;
        var path =
            $"{Encode(project)}/_apis/git/repositories/{Encode(repositoryId)}/items" +
            $"?scopePath={Uri.EscapeDataString(scope)}" +
            $"&recursionLevel=OneLevel" +
            $"&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
            $"&api-version={_options.ApiVersion}";

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return [];
        }

        await EnsureSuccessAsync(response, path).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var collection = JsonSerializer.Deserialize<AdoCollection<AdoItem>>(body, JsonOptions);
        return collection?.Value ?? [];
    }

    public async Task<string?> GetItemContentAsync(
        string project,
        string repositoryId,
        string itemPath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var path =
            $"{Encode(project)}/_apis/git/repositories/{Encode(repositoryId)}/items" +
            $"?path={Uri.EscapeDataString(itemPath)}" +
            $"&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
            $"&includeContent=true" +
            $"&api-version={_options.ApiVersion}";

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, path).ConfigureAwait(false);
        var media = response.Content.Headers.ContentType?.MediaType ?? "";
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (media.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return Truncate(content.GetString());
            }

            if (document.RootElement.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.Array &&
                value.GetArrayLength() > 0 &&
                value[0].TryGetProperty("content", out var nested) &&
                nested.ValueKind == JsonValueKind.String)
            {
                return Truncate(nested.GetString());
            }
        }

        return Truncate(body);
    }

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var url = relativeUrl;
        while (true)
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, url).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var collection = JsonSerializer.Deserialize<AdoCollection<T>>(body, JsonOptions);
            if (collection?.Value is { Count: > 0 })
            {
                results.AddRange(collection.Value);
            }

            if (!response.Headers.TryGetValues(ContinuationTokenHeader, out var tokens))
            {
                break;
            }

            var token = tokens.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                break;
            }

            url = AppendQuery(relativeUrl, "continuationToken", token);
        }

        return results;
    }

    private static string AppendQuery(string url, string name, string value)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}{name}={Uri.EscapeDataString(value)}";
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string? Truncate(string? content)
    {
        if (content is null)
        {
            return null;
        }

        const int maxChars = 128 * 1024;
        return content.Length <= maxChars ? content : content[..maxChars];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var snippet = detail.Length > 300 ? detail[..300] : detail;
        throw new HttpRequestException(
            $"Azure DevOps request failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{path}'. {snippet}");
    }
}
