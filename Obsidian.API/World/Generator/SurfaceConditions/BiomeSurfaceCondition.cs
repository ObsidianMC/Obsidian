namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:biome")]
public sealed record class BiomeSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:biome";

    public required ImmutableArray<string> BiomeIs { get; init; }
}
