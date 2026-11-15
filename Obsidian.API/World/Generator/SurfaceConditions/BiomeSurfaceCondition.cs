namespace Obsidian.API.World.Generator.SurfaceConditions;

/// <summary>
/// Tests whether the surface position belongs to one of the listed biomes.
/// </summary>
[SurfaceCondition("minecraft:biome")]
public sealed record class BiomeSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Gets the surface condition codec type identifier.
    /// </summary>
    public string Type => "minecraft:biome";

    /// <summary>
    /// Gets the biome identifiers accepted by <c>biome_is</c>.
    /// </summary>
    public required ImmutableArray<string> BiomeIs { get; init; }
}
