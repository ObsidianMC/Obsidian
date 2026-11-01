using Obsidian.API.Registries;

namespace Obsidian.API.World.Features;

/// <summary>Version-specific tags in algorithms shared with the retained 1.21.11 replay pipeline.</summary>
internal static class WorldgenTags
{
    [ThreadStatic]
    private static bool legacy;

    public static bool IsLegacy => legacy;

    public static Scope UseLegacy(bool enabled) => new(enabled);

    public readonly struct Scope : IDisposable
    {
        private readonly bool previous;

        public Scope(bool enabled)
        {
            this.previous = legacy;
            legacy = enabled;
        }

        public void Dispose() => legacy = this.previous;
    }

    public static BlockGroup Blocks(string path, string? current = null) => new(path, current);

    public static BiomeGroup Biomes(string path) => new(path);

    internal sealed class BlockGroup(string path, string? currentPath = null)
    {
        private readonly BlockSet current = new($"#minecraft:{currentPath ?? CurrentName(path)}");
        private readonly Lazy<BlockSet> previous = new(() => LegacyWorldgenTags.Blocks(path));

        public BlockSet Selected => legacy ? this.previous.Value : this.current;

        public bool Contains(IBlock block) => this.Selected.Contains(block);
    }

    internal sealed class BiomeGroup(string path)
    {
        private readonly BiomeSet current = new($"#minecraft:{path}");
        private readonly Lazy<BiomeSet> previous = new(() => LegacyWorldgenTags.Biomes(path));

        public bool Contains(Obsidian.API.Registry.Codecs.Biomes.BiomeCodec biome) =>
            (legacy ? this.previous.Value : this.current).Contains(biome);
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
