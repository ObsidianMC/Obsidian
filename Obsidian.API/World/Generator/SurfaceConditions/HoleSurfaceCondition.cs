namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:hole")]
public sealed class HoleSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:hole";
}
