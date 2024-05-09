namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class NotSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:not";

    public ISurfaceCondition Invert { get; set; }
}
