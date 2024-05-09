namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class VerticalGradient : ISurfaceCondition
{
    public string Type => "minecraft:surface_gradient";

    public string RandomName { get; set; }

    public VerticalAnchor TrueAtAndBelow { get; set; }

    public VerticalAnchor FalseAtAndBelow { get; set; }
}
