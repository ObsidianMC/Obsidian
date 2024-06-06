namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:above_preliminary_surface")]
public sealed class AbovePreliminarySurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:above_preliminary_surface";
}
