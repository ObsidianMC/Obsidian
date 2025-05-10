namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:steep")]
public sealed class SteepSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:steep";
}
