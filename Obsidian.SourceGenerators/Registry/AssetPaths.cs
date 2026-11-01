namespace Obsidian.SourceGenerators.Registry;

/// <summary>
/// Locates generated vanilla asset files among a project's additional files.
/// </summary>
internal static class AssetPaths
{
    private const string AssetsFolder = "/Assets/";

    /// <summary>
    /// Gets a file's path relative to <c>Assets/</c> with forward slashes and its extension kept
    /// (e.g. <c>loot_table/chests.json</c>), or null when the file isn't an asset.
    /// </summary>
    /// <remarks>
    /// Anchored on the last <c>/Assets/</c> segment, so a checkout whose own path contains one still resolves the
    /// asset's own folder.
    /// </remarks>
    public static string? GetAssetRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf(AssetsFolder, StringComparison.Ordinal);
        if (index < 0)
            return null;

        return normalized.Substring(index + AssetsFolder.Length);
    }

    /// <summary>
    /// Gets a file's path relative to <c>Assets/worldgen/</c> with forward slashes and its extension kept
    /// (e.g. <c>noise/temperature.json</c>), or null when the file isn't in that folder.
    /// </summary>
    public static string? GetWorldgenRelativePath(string path)
    {
        const string worldgen = "worldgen/";
        var relative = GetAssetRelativePath(path);
        return relative is not null && relative.StartsWith(worldgen, StringComparison.Ordinal)
            ? relative.Substring(worldgen.Length)
            : null;
    }
}
