using System.Text.Json.Serialization;

namespace AdoRepoCatalog.Catalog;

public sealed class CatalogEntry
{
    public string Name { get; set; } = "";

    public string Project { get; set; } = "";

    public string RemoteUrl { get; set; } = "";

    public string DefaultBranch { get; set; } = "";

    public string HeadSha { get; set; } = "";

    public List<string> Languages { get; set; } = [];

    public List<string> Frameworks { get; set; } = [];

    public List<string> Services { get; set; } = [];

    public double Confidence { get; set; }

    public bool LowConfidence { get; set; }

    public DateTimeOffset LastIndexed { get; set; }

    public string WhenToWriteHere { get; set; } = "";

    /// <summary>Path to the wiki page, relative to catalog.json.</summary>
    public string WikiPath { get; set; } = "";

    [JsonIgnore]
    public List<string> GeneratedFromFiles { get; set; } = [];

    [JsonIgnore]
    public string Purpose { get; set; } = "";

    [JsonIgnore]
    public string StackSummary { get; set; } = "";

    [JsonIgnore]
    public string ServicesSummary { get; set; } = "";
}
