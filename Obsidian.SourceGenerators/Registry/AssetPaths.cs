namespace Obsidian.SourceGenerators.Registry;

/// <summary>
/// Locates generated vanilla asset files among a project's additional files.
/// </summary>
internal static class AssetPaths
{
    private const string AssetsFolder = "/Assets/";

    /// <summary>
    /// The prefix of the folder holding an older vanilla version's loot and worldgen assets (<c>Assets/legacy-1.21.11/</c>),
    /// which stand in for the current version's until Obsidian's models read those. Temporary: Phase 4 (loot) and
    /// Phase 5 (worldgen) of <c>docs/porting-26.3-plan.md</c> remove it.
    /// </summary>
    private const string LegacyFolderPrefix = "legacy-";

    /// <summary>
    /// Gets a file's path relative to <c>Assets/</c> with forward slashes and its extension kept
    /// (e.g. <c>loot_table/chests.json</c>), or null when the file isn't an asset. A legacy asset's path is relative to
    /// its legacy folder (see <see cref="LegacyFolderPrefix"/>), like the current version's asset it stands in for.
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

        var relative = normalized.Substring(index + AssetsFolder.Length);
        if (!relative.StartsWith(LegacyFolderPrefix, StringComparison.Ordinal))
            return relative;

        var separator = relative.IndexOf('/');
        return separator < 0 ? null : relative.Substring(separator + 1);
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
