namespace AdoRepoCatalog;

/// <summary>
/// Hard v1 limits: a handful of key files, then discard. Wiki/state stay metadata-only.
/// </summary>
public static class WorkingSetLimits
{
    public const int MaxKeyFilesPerRepo = 16;

    public const int MaxFileContentChars = 16 * 1024;

    public const int MaxExtraShallowFolders = 1;
}
