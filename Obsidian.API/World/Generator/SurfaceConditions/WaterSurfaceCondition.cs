namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:water")]
public sealed record class WaterSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:water";

    public required int Offset { get; init; }

    public required int SurfaceDepthMultiplier { get; init; }

    public required bool AddStoneDepth { get; init; }
}
