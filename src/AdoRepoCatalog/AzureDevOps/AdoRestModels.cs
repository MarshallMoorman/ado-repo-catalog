using System.Text.Json.Serialization;

namespace AdoRepoCatalog.AzureDevOps;

/// <summary>Official Azure DevOps collection wrapper ({ count, value }).</summary>
public sealed class AdoCollection<T>
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];
}

public sealed class AdoProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class AdoRepository
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("defaultBranch")]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }

    [JsonPropertyName("project")]
    public AdoProject? Project { get; set; }
}

public sealed class AdoCommit
{
    [JsonPropertyName("commitId")]
    public string CommitId { get; set; } = "";

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public sealed class AdoItem
{
    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }

    [JsonPropertyName("gitObjectType")]
    public string? GitObjectType { get; set; }

    [JsonPropertyName("commitId")]
    public string? CommitId { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("isFolder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonIgnore]
    public bool IsTree =>
        IsFolder || string.Equals(GitObjectType, "tree", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string NormalizedPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "/";
            }

            return Path.StartsWith('/') ? Path : "/" + Path;
        }
    }

    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(NormalizedPath.TrimEnd('/'));
}
