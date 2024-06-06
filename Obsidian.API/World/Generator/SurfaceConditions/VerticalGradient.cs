namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:surface_gradient")]
public sealed record class VerticalGradient : ISurfaceCondition
{
    public string Type => "minecraft:surface_gradient";

    public required string RandomName { get; init; }

    public required VerticalAnchor TrueAtAndBelow { get; init; }

    public required VerticalAnchor FalseAtAndBelow { get; init; }
}
