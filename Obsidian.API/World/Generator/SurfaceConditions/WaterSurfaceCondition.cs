namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class WaterSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:water";

    public int Offset { get; set; }

    public int SurfaceDepthMultiplier { get; set; }

    public bool AddStoneDepth { get; set; }
}
