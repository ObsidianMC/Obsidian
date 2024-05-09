namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class BiomeSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:biome";

    public string[] BiomeIs { get; set; }
}
