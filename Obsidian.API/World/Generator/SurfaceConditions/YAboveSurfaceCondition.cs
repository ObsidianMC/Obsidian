namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:y_above")]
public sealed record class YAboveSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:y_above";

    public required VerticalAnchor Anchor { get; init; }

    public required int SurfaceDepthMultiplier { get; init; }

    public required bool AddStoneDepth { get; init; }
}
