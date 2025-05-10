namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:vertical_gradient")]
public sealed record class VerticalGradient : ISurfaceCondition
{
    public string Type => "minecraft:vertical_gradient";

    public required string RandomName { get; init; }

    public required VerticalAnchor TrueAtAndBelow { get; init; }

    public required VerticalAnchor FalseAtAndAbove { get; init; }
}
