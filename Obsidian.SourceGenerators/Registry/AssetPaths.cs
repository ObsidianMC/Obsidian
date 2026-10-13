namespace Obsidian.SourceGenerators.Registry;

/// <summary>
/// Locates generated vanilla asset files among a project's additional files.
/// </summary>
internal static class AssetPaths
{
    private const string WorldgenFolder = "/Assets/worldgen/";

    /// <summary>
    /// Gets a file's path relative to <c>Assets/worldgen/</c> with forward slashes and its extension kept
    /// (e.g. <c>noise/temperature.json</c>), or null when the file isn't in that folder.
    /// </summary>
    /// <remarks>
    /// Anchored on the last <c>/Assets/worldgen/</c> segment, so a checkout whose own path contains "worldgen"
    /// (e.g. <c>C:/src/worldgen/Obsidian</c>) still resolves the asset's own folder.
    /// </remarks>
    public static string? GetWorldgenRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf(WorldgenFolder, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return null;

        return normalized.Substring(index + WorldgenFolder.Length);
    }
}
