using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AdoRepoCatalog;
using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Tests;

public sealed class AzureDevOpsClientTests
{
    [Fact]
    public async Task Uses_official_7_1_endpoints_and_basic_pat_auth()
    {
        var handler = new ScriptedHandler
        {
            Respond = request =>
            {
                var url = request.RequestUri!.AbsoluteUri;
                if (url.Contains("/_apis/projects?", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "count": 1,
                          "value": [
                            {
                              "id": "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c",
                              "name": "Fabrikam-Fiber-Git",
                              "description": "Git projects",
                              "url": "https://dev.azure.com/fabrikam/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c",
                              "state": "wellFormed"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/_apis/git/repositories?", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "count": 1,
                          "value": [
                            {
                              "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                              "name": "contoso-demo",
                              "url": "https://dev.azure.com/fabrikam/_apis/git/repositories/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                              "project": {
                                "id": "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c",
                                "name": "Fabrikam-Fiber-Git",
                                "state": "wellFormed"
                              },
                              "defaultBranch": "refs/heads/main",
                              "remoteUrl": "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/commits?", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "count": 1,
                          "value": [
                            {
                              "commitId": "1111111111111111111111111111111111111111",
                              "comment": "seed"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/items?", StringComparison.Ordinal) && url.Contains("includeContent=true", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "objectId": "bbbb",
                          "gitObjectType": "blob",
                          "path": "/README.md",
                          "content": "# contoso-demo\n"
                        }
                        """);
                }

                if (url.Contains("/items?", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "count": 2,
                          "value": [
                            {
                              "objectId": "tree1",
                              "gitObjectType": "tree",
                              "path": "/",
                              "isFolder": true
                            },
                            {
                              "objectId": "blob1",
                              "gitObjectType": "blob",
                              "path": "/README.md"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/_apis/git/repositories/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?", StringComparison.Ordinal))
                {
                    return Json("""
                        {
                          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                          "name": "contoso-demo",
                          "defaultBranch": "refs/heads/main",
                          "remoteUrl": "https://dev.azure.com/fabrikam/Fabrikam-Fiber-Git/_git/contoso-demo",
                          "project": {
                            "id": "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c",
                            "name": "Fabrikam-Fiber-Git",
                            "state": "wellFormed"
                          }
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(url),
                };
            },
        };

        var options = new CatalogOptions
        {
            Organization = "fabrikam",
            PersonalAccessToken = "not-a-real-pat",
            BaseUrl = "https://dev.azure.com",
        };
        using var http = new HttpClient(handler);
        var client = new AzureDevOpsClient(http, options);

        var projects = await client.ListProjectsAsync();
        var repos = await client.ListRepositoriesAsync("Fabrikam-Fiber-Git");
        var details = await client.GetRepositoryAsync("Fabrikam-Fiber-Git", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sha = await client.GetHeadCommitAsync("Fabrikam-Fiber-Git", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "main");
        var items = await client.ListItemsAsync("Fabrikam-Fiber-Git", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "/", "main");
        var content = await client.GetItemContentAsync("Fabrikam-Fiber-Git", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "/README.md", "main");

        Assert.Equal("Fabrikam-Fiber-Git", Assert.Single(projects).Name);
        Assert.Equal("contoso-demo", Assert.Single(repos).Name);
        Assert.Equal("refs/heads/main", details.DefaultBranch);
        Assert.Equal("1111111111111111111111111111111111111111", sha);
        Assert.Contains(items, item => item.Path == "/README.md");
        Assert.Equal("# contoso-demo\n", content);

        Assert.All(handler.Uris, uri => Assert.Contains("api-version=7.1", uri));
        Assert.Contains(handler.Uris, uri => uri.Contains("https://dev.azure.com/fabrikam/_apis/projects?", StringComparison.Ordinal));
        Assert.Contains(handler.Uris, uri => uri.Contains("/Fabrikam-Fiber-Git/_apis/git/repositories?", StringComparison.Ordinal));
        Assert.Contains(handler.Uris, uri => uri.Contains("/_apis/git/repositories/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?", StringComparison.Ordinal));
        Assert.Contains(handler.Uris, uri =>
            uri.Contains("/commits?", StringComparison.Ordinal) &&
            uri.Contains("searchCriteria.$top=1", StringComparison.Ordinal) &&
            uri.Contains("searchCriteria.itemVersion.version=main", StringComparison.Ordinal));
        Assert.Contains(handler.Uris, uri =>
            uri.Contains("scopePath=%2F", StringComparison.Ordinal) &&
            uri.Contains("recursionLevel=OneLevel", StringComparison.Ordinal) &&
            uri.Contains("versionDescriptor.version=main", StringComparison.Ordinal));

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes(":not-a-real-pat"));
        Assert.All(handler.Auth, header =>
        {
            Assert.Equal("Basic", header!.Scheme);
            Assert.Equal(expected, header.Parameter);
        });
    }

    [Fact]
    public async Task Uses_bearer_auth_when_only_access_token_is_configured()
    {
        var handler = new ScriptedHandler
        {
            Respond = _ => Json("""{ "count": 0, "value": [] }"""),
        };
        var options = new CatalogOptions
        {
            Organization = "fabrikam",
            AccessToken = "not-a-real-bearer",
        };
        using var http = new HttpClient(handler);
        var client = new AzureDevOpsClient(http, options);
        await client.ListProjectsAsync();

        var header = Assert.Single(handler.Auth);
        Assert.Equal("Bearer", header!.Scheme);
        Assert.Equal("not-a-real-bearer", header.Parameter);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new(HttpStatusCode.NotFound);

        public List<string> Uris { get; } = [];

        public List<AuthenticationHeaderValue?> Auth { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.AbsoluteUri);
            Auth.Add(request.Headers.Authorization);
            return Task.FromResult(Respond(request));
        }
    }
}
