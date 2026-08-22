using AdoRepoCatalog.AzureDevOps;

namespace AdoRepoCatalog.Catalog;

public static class KeyFileSelector
{
    public const int MaxFilesToFetch = WorkingSetLimits.MaxKeyFilesPerRepo;

    private static readonly string[] ShallowFolderPriority =
    [
        "src", "source", "app", "apps", "api", "services", "backend", "web", "lib",
    ];

    public static bool IsKeyFile(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.StartsWith("README", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("pipeline", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith("azure-pipelines", StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("go.mod", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsShallowScanFolder(string folderName)
        => ShallowFolderPriority.Contains(folderName, StringComparer.OrdinalIgnoreCase);

    public static string? ChooseShallowFolder(IEnumerable<AdoItem> rootItems)
    {
        var folders = rootItems
            .Where(item => item.IsTree && item.NormalizedPath is not "/")
            .Select(item => item.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in ShallowFolderPriority)
        {
            if (folders.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> SelectFetchPaths(IEnumerable<AdoItem> items)
    {
        return items
            .Where(item => !item.IsTree && IsKeyFile(item.NormalizedPath))
            .Select(item => item.NormalizedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFilesToFetch)
            .ToArray();
    }
}
