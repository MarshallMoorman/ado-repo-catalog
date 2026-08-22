using System.Net;
using System.Text;
using AdoRepoCatalog;
using AdoRepoCatalog.AzureDevOps;
using AdoRepoCatalog.Infrastructure;

namespace AdoRepoCatalog.Tests;

public sealed class RateLimitTests
{
    [Fact]
    public async Task Retries_429_using_retry_after_seconds()
    {
        var delay = new RecordingAsyncDelay();
        var attempts = 0;
        var handler = new ScriptedHandler
        {
            Respond = _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    var tooMany = Json(HttpStatusCode.TooManyRequests, """{ "message": "slow down" }""");
                    tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                    return tooMany;
                }

                return Json(HttpStatusCode.OK, """{ "count": 0, "value": [] }""");
            },
        };

        using var http = AzureDevOpsClient.CreateHttpClient(ThrottleOptions(), delay, innerHandler: handler);
        var client = new AzureDevOpsClient(http, ThrottleOptions());
        var projects = await client.ListProjectsAsync();

        Assert.Empty(projects);
        Assert.Equal(2, attempts);
        Assert.Contains(TimeSpan.FromSeconds(2), delay.Delays);
    }

    [Fact]
    public async Task Backs_off_exponentially_when_429_has_no_retry_after()
    {
        var delay = new RecordingAsyncDelay();
        var attempts = 0;
        var handler = new ScriptedHandler
        {
            Respond = _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    return Json(HttpStatusCode.TooManyRequests, """{ "message": "busy" }""");
                }

                return Json(HttpStatusCode.OK, """{ "count": 0, "value": [] }""");
            },
        };

        var options = ThrottleOptions();
        options.RetryJitterRatio = 0;
        options.RetryBaseDelay = TimeSpan.FromSeconds(1);
        using var http = AzureDevOpsClient.CreateHttpClient(options, delay, innerHandler: handler);
        var client = new AzureDevOpsClient(http, options);
        await client.ListProjectsAsync();

        Assert.Equal(3, attempts);
        Assert.Equal(TimeSpan.FromSeconds(1), delay.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), delay.Delays[1]);
    }

    [Fact]
    public async Task Http_200_with_rate_limit_delay_waits_before_next_call()
    {
        var delay = new RecordingAsyncDelay();
        var handler = new ScriptedHandler
        {
            Respond = _ =>
            {
                var ok = Json(HttpStatusCode.OK, """{ "count": 0, "value": [] }""");
                ok.Headers.TryAddWithoutValidation(AdoThrottlingHandler.RateLimitDelayHeader, "5");
                ok.Headers.TryAddWithoutValidation(AdoThrottlingHandler.RateLimitRemainingHeader, "0");
                return ok;
            },
        };

        using var http = AzureDevOpsClient.CreateHttpClient(ThrottleOptions(), delay, innerHandler: handler);
        var client = new AzureDevOpsClient(http, ThrottleOptions());
        await client.ListProjectsAsync();
        await client.ListProjectsAsync();

        Assert.Contains(TimeSpan.FromSeconds(5), delay.Delays);
    }

    [Fact]
    public async Task Repo_concurrency_is_capped_between_2_and_4()
    {
        var client = FabrikamFixture.CreateClient();
        AddSiblingRepos(client, count: 4);
        client.RepositoryHold = TimeSpan.FromMilliseconds(40);

        using var host = new CatalogTestHost(client);
        host.Options.MaxConcurrency = 2;
        Assert.Equal(2, host.Options.EffectiveMaxConcurrency);

        await host.Runner.RunAsync();

        Assert.InRange(client.PeakInFlight, 1, 2);
        Assert.True(client.PeakInFlight <= 2);
    }

    [Fact]
    public async Task Rejects_non_get_requests()
    {
        var handler = new ScriptedHandler
        {
            Respond = _ => Json(HttpStatusCode.OK, """{ "count": 0, "value": [] }"""),
        };
        using var http = AzureDevOpsClient.CreateHttpClient(ThrottleOptions(), new RecordingAsyncDelay(), innerHandler: handler);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => http.PostAsync("_apis/projects?api-version=7.1", new StringContent("{}")));
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Max_concurrency_clamps_to_the_2_to_4_band()
    {
        Assert.Equal(3, new CatalogOptions().EffectiveMaxConcurrency);
        Assert.Equal(2, new CatalogOptions { MaxConcurrency = 1 }.EffectiveMaxConcurrency);
        Assert.Equal(4, new CatalogOptions { MaxConcurrency = 99 }.EffectiveMaxConcurrency);
        Assert.Equal(3, new CatalogOptions { MaxConcurrency = 3 }.EffectiveMaxConcurrency);
    }

    private static CatalogOptions ThrottleOptions() => new()
    {
        Organization = "fabrikam",
        PersonalAccessToken = "not-a-real-pat",
        RetryJitterRatio = 0,
        MaxRetries = 5,
        MaxConcurrency = 3,
    };

    private static void AddSiblingRepos(FakeAzureDevOpsClient client, int count)
    {
        var project = client.Projects[0];
        for (var i = 0; i < count; i++)
        {
            var id = $"eeeeeeee-eeee-eeee-eeee-eeeeeeeeee{i:D2}";
            client.Repositories.Add(new AdoRepoCatalog.AzureDevOps.AdoRepository
            {
                Id = id,
                Name = $"contoso-slot-{i}",
                DefaultBranch = "refs/heads/main",
                RemoteUrl = $"https://dev.azure.com/fabrikam/{project.Name}/_git/contoso-slot-{i}",
                Project = project,
            });
            client.SetHead(id, "main", new string((char)('a' + i), 40));
            client.AddItems(id, "main", "/", FabrikamFixture.File("/README.md"));
            client.AddFile(id, "main", "/README.md", "Tiny fictional slot used only to exercise concurrency.");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Respond(request));
    }
}
