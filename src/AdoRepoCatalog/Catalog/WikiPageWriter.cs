using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AdoRepoCatalog.Catalog;

public static partial class WikiPageWriter
{
    public const string OverrideStart = "<!-- OVERRIDE:START -->";
    public const string OverrideEnd = "<!-- OVERRIDE:END -->";
    public const string OverrideHeading = "## Human override";

    [GeneratedRegex(@"^---\s*$", RegexOptions.Multiline)]
    private static partial Regex FrontMatterFence();

    public static string FileName(string project, string repo) => $"{Slug.From(project, repo)}.md";

    public static string Compose(CatalogEntry entry, string? existingPage)
    {
        var builder = new StringBuilder();
        WriteFrontMatter(builder, entry);
        builder.AppendLine();
        builder.Append("# ").AppendLine(entry.Name);
        builder.AppendLine();
        builder.AppendLine("## Purpose");
        builder.AppendLine();
        builder.AppendLine(entry.Purpose);
        builder.AppendLine();
        builder.AppendLine("## Stack");
        builder.AppendLine();
        builder.AppendLine(entry.StackSummary);
        builder.AppendLine();
        builder.AppendLine("## Services / functionality");
        builder.AppendLine();
        builder.AppendLine(entry.ServicesSummary);
        builder.AppendLine();
        builder.AppendLine("## Routing guidance");
        builder.AppendLine();
        builder.AppendLine(entry.WhenToWriteHere);
        builder.AppendLine();
        builder.AppendLine("## Generated from files");
        builder.AppendLine();
        if (entry.GeneratedFromFiles.Count == 0)
        {
            builder.AppendLine("No key files were fetched.");
        }
        else
        {
            foreach (var path in entry.GeneratedFromFiles)
            {
                builder.Append("- `").Append(path).AppendLine("`");
            }
        }

        builder.AppendLine();
        if (entry.LowConfidence)
        {
            builder.AppendLine(
                "> Low-confidence catalog entry: few useful signals were found. Treat this as a starting point, not a hold.");
            builder.AppendLine();
        }

        builder.AppendLine(ComposeOverride(existingPage).TrimEnd());
        builder.AppendLine();
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string ComposeOverride(string? existingPage)
    {
        var extracted = ExtractOverride(existingPage);
        if (extracted is null)
        {
            return DefaultOverride();
        }

        return extracted.StartsWith(OverrideHeading, StringComparison.Ordinal)
            ? extracted.TrimEnd()
            : $"{OverrideHeading}\n\n{extracted.TrimEnd()}";
    }

    public static string? ExtractOverride(string? existingPage)
    {
        if (string.IsNullOrWhiteSpace(existingPage))
        {
            return null;
        }

        var start = existingPage.IndexOf(OverrideStart, StringComparison.Ordinal);
        var end = existingPage.IndexOf(OverrideEnd, StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            var block = existingPage[start..(end + OverrideEnd.Length)];
            var heading = existingPage.LastIndexOf(OverrideHeading, start, StringComparison.Ordinal);
            if (heading >= 0)
            {
                return existingPage[heading..(end + OverrideEnd.Length)].TrimEnd();
            }

            return block.TrimEnd();
        }

        var headingOnly = existingPage.IndexOf(OverrideHeading, StringComparison.Ordinal);
        if (headingOnly >= 0)
        {
            return existingPage[headingOnly..].TrimEnd();
        }

        return null;
    }

    public static string DefaultOverride() =>
        $"""
        {OverrideHeading}

        {OverrideStart}
        <!-- Add durable notes below. This block is preserved on refresh. -->
        {OverrideEnd}
        """;

    public static void Write(string wikiDirectory, CatalogEntry entry, string? existingPage)
    {
        Directory.CreateDirectory(wikiDirectory);
        var fileName = Path.GetFileName(entry.WikiPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = FileName(entry.Project, entry.Name);
        }

        var path = Path.Combine(wikiDirectory, fileName);
        File.WriteAllText(path, Compose(entry, existingPage));
    }

    public static bool TryReadEntry(string wikiPath, string relativeWikiPath, out CatalogEntry entry)
    {
        entry = new CatalogEntry();
        if (!File.Exists(wikiPath))
        {
            return false;
        }

        var text = File.ReadAllText(wikiPath);
        if (!TryParseFrontMatter(text, out entry))
        {
            return false;
        }

        entry.WikiPath = relativeWikiPath;
        return true;
    }

    public static bool TryParseFrontMatter(string markdown, out CatalogEntry entry)
    {
        entry = new CatalogEntry();
        var fences = FrontMatterFence().Matches(markdown);
        if (fences.Count < 2)
        {
            return false;
        }

        var start = fences[0].Index + fences[0].Length;
        var yaml = markdown[start..fences[1].Index];
        string? currentList = null;
        foreach (var rawLine in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                currentList = null;
                continue;
            }

            if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal) && currentList is not null)
            {
                var item = Unquote(line.Trim()[2..].Trim());
                ListFor(entry, currentList).Add(item);
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            currentList = null;
            switch (key)
            {
                case "name":
                    entry.Name = Unquote(value);
                    break;
                case "project":
                    entry.Project = Unquote(value);
                    break;
                case "remoteUrl":
                    entry.RemoteUrl = Unquote(value);
                    break;
                case "defaultBranch":
                    entry.DefaultBranch = Unquote(value);
                    break;
                case "headSha":
                    entry.HeadSha = Unquote(value);
                    break;
                case "whenToWriteHere":
                    entry.WhenToWriteHere = Unquote(value);
                    break;
                case "confidence":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
                    {
                        entry.Confidence = confidence;
                    }

                    break;
                case "lowConfidence":
                    entry.LowConfidence = bool.TryParse(value, out var low) && low;
                    break;
                case "lastIndexed":
                    if (DateTimeOffset.TryParse(Unquote(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                    {
                        entry.LastIndexed = when;
                    }

                    break;
                case "languages":
                case "frameworks":
                case "services":
                    currentList = key;
                    break;
            }
        }

        return !string.IsNullOrWhiteSpace(entry.Name);
    }

    private static List<string> ListFor(CatalogEntry entry, string key) => key switch
    {
        "languages" => entry.Languages,
        "frameworks" => entry.Frameworks,
        "services" => entry.Services,
        _ => entry.Services,
    };

    private static void WriteFrontMatter(StringBuilder builder, CatalogEntry entry)
    {
        builder.AppendLine("---");
        builder.Append("name: ").AppendLine(Quote(entry.Name));
        builder.Append("project: ").AppendLine(Quote(entry.Project));
        builder.Append("remoteUrl: ").AppendLine(Quote(entry.RemoteUrl));
        builder.Append("defaultBranch: ").AppendLine(Quote(entry.DefaultBranch));
        builder.Append("headSha: ").AppendLine(Quote(entry.HeadSha));
        WriteList(builder, "languages", entry.Languages);
        WriteList(builder, "frameworks", entry.Frameworks);
        WriteList(builder, "services", entry.Services);
        builder.Append("confidence: ").AppendLine(entry.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
        builder.Append("lowConfidence: ").AppendLine(entry.LowConfidence ? "true" : "false");
        builder.Append("lastIndexed: ").AppendLine(Quote(entry.LastIndexed.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        builder.Append("whenToWriteHere: ").AppendLine(Quote(entry.WhenToWriteHere));
        builder.AppendLine("---");
    }

    private static void WriteList(StringBuilder builder, string name, IReadOnlyList<string> items)
    {
        builder.Append(name).AppendLine(":");
        if (items.Count == 0)
        {
            builder.AppendLine("  []");
            return;
        }

        foreach (var item in items)
        {
            builder.Append("  - ").AppendLine(Quote(item));
        }
    }

    private static string Quote(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }
}
