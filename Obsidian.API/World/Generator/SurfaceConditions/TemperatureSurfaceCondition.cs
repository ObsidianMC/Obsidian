namespace Obsidian.API.World.Generator.SurfaceConditions;

[SurfaceCondition("minecraft:temperature")]
public sealed class TemperatureSurfaceCondition : ISurfaceCondition
{
    public string Type => "minecraft:temperature";
}
