namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:stone_depth")]
public sealed record class StoneDepthSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:stone_depth";

    public required int Offset { get; init; }

    public required bool AddSurfaceDepth { get; init; }

    public required int SecondaryDepthRange { get; init; }

    public required string SurfaceType { get; init; }
}
