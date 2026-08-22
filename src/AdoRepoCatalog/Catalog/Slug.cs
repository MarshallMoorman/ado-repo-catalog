using System.Text;
using System.Text.RegularExpressions;

namespace AdoRepoCatalog.Catalog;

public static partial class Slug
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex RepeatedDash();

    public static string From(string project, string repo)
        => Combine(From(project), From(repo));

    public static string From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        var lower = value.Trim().ToLowerInvariant();
        var collapsed = NonSlugChars().Replace(lower, "-");
        collapsed = RepeatedDash().Replace(collapsed, "-").Trim('-');
        if (collapsed.Length == 0)
        {
            return "unnamed";
        }

        return collapsed.Length <= 80 ? collapsed : collapsed[..80].TrimEnd('-');
    }

    public static string Combine(params string[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('-');
            }

            builder.Append(part);
        }

        var combined = builder.ToString();
        return combined.Length == 0 ? "unnamed" : combined;
    }
}
