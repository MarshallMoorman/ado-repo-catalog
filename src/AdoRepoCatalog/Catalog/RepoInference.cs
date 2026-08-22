using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Catalog;

public sealed class RepoSnapshot
{
    public required AdoRepository Repository { get; init; }

    public required string Branch { get; init; }

    public required string HeadSha { get; init; }

    public required IReadOnlyList<AdoItem> TreeEntries { get; init; }

    public required IReadOnlyDictionary<string, string> Files { get; init; }
}

public static partial class RepoInference
{
    public const double LowConfidenceThreshold = 0.4;

    [GeneratedRegex(@"Controller\.cs$", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerFile();

    public static CatalogEntry Infer(RepoSnapshot snapshot, DateTimeOffset lastIndexed)
    {
        var repo = snapshot.Repository;
        var project = repo.Project?.Name ?? "";
        var files = snapshot.Files;
        var paths = files.Keys.Concat(snapshot.TreeEntries.Select(item => item.NormalizedPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var services = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var signals = new List<string>();

        var readme = FindFile(files, name => name.StartsWith("README", StringComparison.OrdinalIgnoreCase));
        var agents = FindFile(files, name => name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase));
        var readmeText = UsefulText(readme.Value);
        var agentsText = UsefulText(agents.Value);

        if (readmeText is not null)
        {
            signals.Add("readme");
        }

        if (agentsText is not null)
        {
            signals.Add("agents");
        }

        foreach (var (path, content) in files)
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                InferDotNet(name, content, languages, frameworks, services, signals);
            }
            else if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            {
                InferPackageJson(content, languages, frameworks, services, signals);
            }
            else if (name.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("go");
                signals.Add("gomod");
                InferGoMod(content, services);
            }
            else if (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("python");
                signals.Add("pyproject");
                InferPyproject(content, frameworks, services);
            }
            else if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("docker");
                signals.Add("docker");
                InferDockerfile(content, frameworks, services);
            }
            else if (name.Contains("pipeline", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("azure-pipelines", StringComparison.OrdinalIgnoreCase))
            {
                signals.Add("pipeline");
            }
            else if (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            {
                InferAppSettingsKeys(content, services, signals);
            }
            else if (name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("csharp");
                signals.Add("entrypoint");
                InferCsharpEntry(name, content, frameworks, services);
            }
        }

        InferFromFolders(snapshot.TreeEntries, services, signals);

        var purpose = InferPurpose(repo.Name, readmeText, agentsText, frameworks, services);
        var whenToWrite = InferWhenToWrite(purpose, frameworks, services);
        var confidence = Score(signals);
        var low = confidence < LowConfidenceThreshold;

        return new CatalogEntry
        {
            Name = repo.Name,
            Project = project,
            RemoteUrl = repo.RemoteUrl ?? repo.WebUrl ?? "",
            DefaultBranch = snapshot.Branch,
            HeadSha = snapshot.HeadSha,
            Languages = [.. languages],
            Frameworks = [.. frameworks],
            Services = [.. services],
            Confidence = confidence,
            LowConfidence = low,
            LastIndexed = lastIndexed,
            WhenToWriteHere = whenToWrite,
            GeneratedFromFiles = [.. files.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)],
            Purpose = purpose,
            StackSummary = FormatStack(languages, frameworks),
            ServicesSummary = FormatServices(services, paths),
        };
    }

    public static double Score(IReadOnlyCollection<string> signals)
    {
        var unique = signals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        double score = 0;
        if (unique.Contains("readme")) score += 0.25;
        if (unique.Contains("agents")) score += 0.15;
        if (unique.Contains("dotnet") || unique.Contains("packagejson") || unique.Contains("gomod") ||
            unique.Contains("pyproject"))
        {
            score += 0.15;
        }

        if (unique.Contains("docker")) score += 0.10;
        if (unique.Contains("pipeline")) score += 0.10;
        if (unique.Contains("entrypoint")) score += 0.10;
        if (unique.Contains("services")) score += 0.10;
        if (unique.Contains("folders")) score += 0.05;
        if (unique.Contains("appsettings")) score += 0.05;

        if (unique.Count == 0)
        {
            score = 0.08;
        }

        return Math.Min(1.0, Math.Round(score, 2));
    }

    private static KeyValuePair<string, string> FindFile(
        IReadOnlyDictionary<string, string> files,
        Func<string, bool> matchName)
    {
        var match = files.FirstOrDefault(pair => matchName(Path.GetFileName(pair.Key)));
        return match;
    }

    private static string? UsefulText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("[![", StringComparison.Ordinal))
            .Where(line => !line.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lines.Count == 0)
        {
            return null;
        }

        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(string.Join(' ', current));
                    current.Clear();
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(string.Join(' ', current));
        }

        var text = paragraphs.FirstOrDefault(p => p.Length >= 24) ?? paragraphs.FirstOrDefault();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void InferDotNet(
        string fileName,
        string content,
        ISet<string> languages,
        ISet<string> frameworks,
        ISet<string> services,
        ICollection<string> signals)
    {
        languages.Add("csharp");
        signals.Add("dotnet");

        if (fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("dotnet-solution");
            return;
        }

        try
        {
            var xml = XDocument.Parse(content);
            var sdk = xml.Root?.Attribute("Sdk")?.Value ?? "";
            if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("aspnetcore");
            }
            else if (sdk.Contains("Worker", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("dotnet-worker");
            }
            else if (sdk.Length > 0)
            {
                frameworks.Add("dotnet");
            }

            var tfm = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value;
            if (!string.IsNullOrWhiteSpace(tfm))
            {
                frameworks.Add(tfm.Trim());
            }

            var description = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Description")?.Value;
            if (!string.IsNullOrWhiteSpace(description) && description.Length < 200)
            {
                services.Add(CompactToken(description));
                signals.Add("services");
            }
        }
        catch (System.Xml.XmlException)
        {
            frameworks.Add("dotnet");
        }
    }

    private static void InferPackageJson(
        string content,
        ISet<string> languages,
        ISet<string> frameworks,
        ISet<string> services,
        ICollection<string> signals)
    {
        languages.Add("javascript");
        signals.Add("packagejson");
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("description", out var description) &&
                description.ValueKind == JsonValueKind.String &&
                UsefulText(description.GetString()) is { } text)
            {
                services.Add(CompactToken(text));
                signals.Add("services");
            }

            AddDeps(root, "dependencies", languages, frameworks);
            AddDeps(root, "devDependencies", languages, frameworks);
        }
        catch (JsonException)
        {
            // ignore malformed package.json
        }
    }

    private static void AddDeps(
        JsonElement root,
        string property,
        ISet<string> languages,
        ISet<string> frameworks)
    {
        if (!root.TryGetProperty(property, out var deps) || deps.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var dep in deps.EnumerateObject())
        {
            var name = dep.Name;
            if (name.Equals("typescript", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("typescript");
            }
            else if (name.Equals("react", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("react-", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("react");
            }
            else if (name.Equals("next", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("nextjs");
            }
            else if (name.Equals("express", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("express");
            }
            else if (name.StartsWith("@nestjs/", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("nestjs");
            }
            else if (name.Equals("vue", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("vue");
            }
        }
    }

    private static void InferGoMod(string content, ISet<string> services)
    {
        var first = content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("module ", StringComparison.Ordinal));
        if (first is null)
        {
            return;
        }

        var module = first["module ".Length..].Trim();
        var leaf = module.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(leaf))
        {
            services.Add(leaf);
        }
    }

    private static void InferPyproject(string content, ISet<string> frameworks, ISet<string> services)
    {
        if (content.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("fastapi");
        }

        if (content.Contains("django", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("django");
        }

        if (content.Contains("flask", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("flask");
        }

        var nameLine = content.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("name", StringComparison.OrdinalIgnoreCase) && l.Contains('='));
        if (nameLine is not null)
        {
            var value = nameLine.Split('=', 2)[1].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(value))
            {
                services.Add(value);
            }
        }
    }

    private static void InferDockerfile(string content, ISet<string> frameworks, ISet<string> services)
    {
        if (content.Contains("aspnet", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("aspnetcore");
        }

        if (content.Contains("node", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("node");
        }

        if (content.Contains("python", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("python");
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("EXPOSE", StringComparison.OrdinalIgnoreCase))
            {
                services.Add("http");
            }
        }
    }

    private static void InferAppSettingsKeys(string content, ISet<string> services, ICollection<string> signals)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = property.Name;
                if (name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
                {
                    services.Add("database");
                    signals.Add("appsettings");
                    signals.Add("services");
                }
                else if (name.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase))
                {
                    services.Add("servicebus");
                    signals.Add("appsettings");
                    signals.Add("services");
                }
                else if (name.Contains("Redis", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Cache", StringComparison.OrdinalIgnoreCase))
                {
                    services.Add("cache");
                    signals.Add("appsettings");
                    signals.Add("services");
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }
    }

    private static void InferCsharpEntry(string fileName, string content, ISet<string> frameworks, ISet<string> services)
    {
        if (content.Contains("WebApplication", StringComparison.Ordinal) ||
            content.Contains("MapControllers", StringComparison.Ordinal) ||
            content.Contains("MapGet", StringComparison.Ordinal))
        {
            frameworks.Add("aspnetcore");
        }

        if (ControllerFile().IsMatch(fileName))
        {
            var service = fileName[..^"Controller.cs".Length];
            if (!string.IsNullOrWhiteSpace(service))
            {
                services.Add(ToKebab(service));
            }
        }
    }

    private static void InferFromFolders(IEnumerable<AdoItem> items, ISet<string> services, ICollection<string> signals)
    {
        var names = items
            .Where(item => item.IsTree)
            .Select(item => item.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var name in names)
        {
            if (name.Equals("controllers", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                services.Add("http-api");
                signals.Add("folders");
                signals.Add("services");
            }
        }
    }

    private static string InferPurpose(
        string repoName,
        string? readme,
        string? agents,
        IReadOnlyCollection<string> frameworks,
        IReadOnlyCollection<string> services)
    {
        if (readme is not null)
        {
            return readme;
        }

        if (agents is not null)
        {
            return agents;
        }

        var stack = frameworks.Count > 0 ? string.Join(", ", frameworks) : "unknown stack";
        var serviceText = services.Count > 0
            ? " Services: " + string.Join(", ", services) + "."
            : "";
        return $"Purpose was not documented in README. Inferred {stack} repository '{repoName}'.{serviceText}";
    }

    private static string InferWhenToWrite(
        string purpose,
        IReadOnlyCollection<string> frameworks,
        IReadOnlyCollection<string> services)
    {
        if (frameworks.Contains("aspnetcore") || frameworks.Contains("express") || frameworks.Contains("fastapi") ||
            frameworks.Contains("nestjs"))
        {
            return "Add or change HTTP endpoints, application services, and API contracts in this repository.";
        }

        if (frameworks.Contains("react") || frameworks.Contains("nextjs") || frameworks.Contains("vue"))
        {
            return "Add UI and client-side changes for this application here.";
        }

        if (frameworks.Contains("dotnet") && !frameworks.Contains("aspnetcore"))
        {
            return "Add shared library or worker code that belongs to this repository here.";
        }

        if (services.Count > 0)
        {
            return $"Add changes related to {string.Join(", ", services)} here.";
        }

        if (purpose.Length > 0 && !purpose.StartsWith("Purpose was not documented", StringComparison.Ordinal))
        {
            return "Add changes that belong to this repository's documented purpose here.";
        }

        return "Add changes that belong to this repository's purpose and services here. Confirm with a human if confidence is low.";
    }

    private static string FormatStack(IReadOnlyCollection<string> languages, IReadOnlyCollection<string> frameworks)
    {
        var parts = new List<string>();
        if (languages.Count > 0)
        {
            parts.Add("Languages: " + string.Join(", ", languages));
        }

        if (frameworks.Count > 0)
        {
            parts.Add("Frameworks: " + string.Join(", ", frameworks));
        }

        return parts.Count == 0
            ? "Stack could not be inferred from the files that were fetched."
            : string.Join(". ", parts) + ".";
    }

    private static string FormatServices(IReadOnlyCollection<string> services, IReadOnlyCollection<string> paths)
    {
        if (services.Count > 0)
        {
            return "Inferred services / functionality: " + string.Join(", ", services) + ".";
        }

        var hint = paths.FirstOrDefault(path =>
            path.Contains("src", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("app", StringComparison.OrdinalIgnoreCase));
        return hint is null
            ? "No services could be inferred from the fetched files."
            : "No named services were inferred. Inspect fetched project files for functionality.";
    }

    private static string CompactToken(string text)
    {
        var first = text.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        return first.Length <= 80 ? first : first[..80];
    }

    private static string ToKebab(string value)
    {
        var chars = new List<char>();
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && chars.Count > 0)
            {
                chars.Add('-');
            }

            if (char.IsLetterOrDigit(ch))
            {
                chars.Add(char.ToLowerInvariant(ch));
            }
        }

        return new string(chars.ToArray());
    }
}
