using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes the worldgen assets: the current version's worldgen data pack registries, which the generator reads at
/// runtime.
/// </summary>
internal static class WorldgenAssets
{
    /// <summary>
    /// Copies the current version's density, terrain and feature registries to <c>Assets/worldgen_26_3</c>, per file
    /// in the data pack layout (<c>noise/temperature.json</c>). Biomes supply material inputs and feature lists; biome
    /// selection is a later step.
    /// </summary>
    /// <remarks>
    /// The folder name keeps these out of the source generators' <c>worldgen/</c> assets: the generator reads them at
    /// runtime (see <c>WorldgenData</c>).
    /// </remarks>
    public static void WriteRouterData(string dataDirectory, string outputDirectory)
    {
        var worldgen = Path.Combine(dataDirectory, "worldgen");

        foreach (var registry in new[]
        {
            "density_function", "noise", "noise_settings", "material_rule", "material_condition", "carver", "biome",
            "feature", "placed_feature", "block_state_provider", "processor_list", "structure", "structure_set",
            "template_pool"
        })
        {
            CopyDirectory(Path.Combine(worldgen, registry), Path.Combine(outputDirectory, registry));
        }

        CopyDirectory(
            Path.Combine(dataDirectory, "tags", "block"),
            Path.Combine(outputDirectory, "block_tag"));
    }
}
