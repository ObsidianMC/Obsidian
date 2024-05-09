namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class YAboveSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:y_above";

    public VerticalAnchor Anchor { get; set; }

    public int SurfaceDepthMultiplier { get; set; }

    public bool AddStoneDepth { get; set; }
}
