namespace Obsidian.API.World.Features;

/// <summary>
/// The block and biome tags worldgen code reads by name, under the names 26.3 gives them.
/// </summary>
internal static class WorldgenTags
{
    /// <summary>
    /// A block tag by its worldgen name, which 26.3 may have renamed (see <see cref="CurrentName"/>).
    /// </summary>
    /// <param name="path">The tag's name, under <c>minecraft</c>.</param>
    /// <param name="current">The tag's 26.3 name, when it isn't <paramref name="path"/> or its usual renaming.</param>
    public static BlockGroup Blocks(string path, string? current = null) => new(path, current);

    /// <summary>
    /// A biome tag by name.
    /// </summary>
    public static BiomeGroup Biomes(string path) => new(path);

    internal sealed class BlockGroup(string path, string? currentPath = null)
    {
        public BlockSet Selected { get; } = new($"#minecraft:{currentPath ?? CurrentName(path)}");

        public bool Contains(IBlock block) => this.Selected.Contains(block);
    }

    internal sealed class BiomeGroup(string path)
    {
        private readonly BiomeSet current = new($"#minecraft:{path}");

        public bool Contains(Obsidian.API.Registry.Codecs.Biomes.BiomeCodec biome) => this.current.Contains(biome);
    }

    private static string CurrentName(string path) => path switch
    {
        "mushroom_grow_block" => "overrides_mushroom_light_requirement",
        "dry_vegetation_may_place_on" => "supports_dry_vegetation",
        "small_dripleaf_placeable" => "supports_small_dripleaf",
        "big_dripleaf_placeable" => "supports_big_dripleaf",
        "bamboo_plantable_on" => "supports_bamboo",
        "snow_layer_cannot_survive_on" => "cannot_support_snow_layer",
        "snow_layer_can_survive_on" => "support_override_snow_layer",
        _ => path
    };
}
