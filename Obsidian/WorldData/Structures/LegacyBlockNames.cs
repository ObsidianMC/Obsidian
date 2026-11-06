namespace Obsidian.WorldData.Structures;

/// <summary>
/// The block renames vanilla's data fixers apply to a structure template's palette when it loads a template saved by
/// an older version. Data packs keep old templates: Terralith's are from 1.20.1 and name <c>minecraft:grass</c> and
/// <c>minecraft:chain</c>, which 26.3 calls <c>minecraft:short_grass</c> and <c>minecraft:iron_chain</c>.
/// </summary>
/// <remarks>
/// Only the renames since 1.14 are here; templates older than that need vanilla's flattening, which Obsidian doesn't
/// do. The data versions are vanilla's, since a template's own <c>DataVersion</c> decides which renames apply.
/// </remarks>
internal static class LegacyBlockNames
{
    // Vanilla's block renaming fixers in the order they run: a template saved before a fixer's version gets its rename.
    private static readonly (int Version, string From, string To)[] renames =
    [
        (1802, "minecraft:stone_slab", "minecraft:smooth_stone_slab"),
        (1802, "minecraft:sign", "minecraft:oak_sign"),
        (1802, "minecraft:wall_sign", "minecraft:oak_wall_sign"),
        (2209, "minecraft:bee_hive", "minecraft:beehive"),
        (2508, "minecraft:warped_fungi", "minecraft:warped_fungus"),
        (2508, "minecraft:crimson_fungi", "minecraft:crimson_fungus"),
        (2528, "minecraft:soul_fire_torch", "minecraft:soul_torch"),
        (2528, "minecraft:soul_fire_wall_torch", "minecraft:soul_wall_torch"),
        (2528, "minecraft:soul_fire_lantern", "minecraft:soul_lantern"),
        (2680, "minecraft:grass_path", "minecraft:dirt_path"),
        (2700, "minecraft:cave_vines_head", "minecraft:cave_vines"),
        (2700, "minecraft:cave_vines_body", "minecraft:cave_vines_plant"),
        (2717, "minecraft:azalea_leaves_flowers", "minecraft:flowering_azalea_leaves"),
        (3692, "minecraft:grass", "minecraft:short_grass"),
        (4541, "minecraft:chain", "minecraft:iron_chain")
    ];

    /// <summary>
    /// A block's name as 26.3 calls it, given the name a template saved at <paramref name="dataVersion"/> has.
    /// </summary>
    /// <param name="dataVersion">
    /// The template's <c>DataVersion</c>, or null for a template without one, whose names are taken as current:
    /// Obsidian's own templates don't save it.
    /// </param>
    public static string Upgrade(string name, int? dataVersion)
    {
        if (dataVersion is not int version)
            return name;

        foreach (var (fixedIn, from, to) in renames)
        {
            if (version < fixedIn && name == from)
                name = to;
        }

        return name;
    }
}
