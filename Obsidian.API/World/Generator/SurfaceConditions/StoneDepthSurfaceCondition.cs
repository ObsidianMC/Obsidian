namespace Obsidian.API.World.Generator.SurfaceConditions;
public sealed record class StoneDepthSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:stone_depth";

    public int Offset { get; set; }

    public bool AddSurfaceDepth { get; set; }

    public int SecondaryDepthRange { get; set; }

    public string SurfaceType { get; set; }
}
