namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:not")]
public sealed record class NotSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:not";

    public required ISurfaceCondition Invert { get; init; }
}
