namespace Obsidian.API.World.Generator.SurfaceRules;
public sealed class ConditionSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:condition";

    public ISurfaceCondition IfTrue { get; set; }

    public ISurfaceRule ThenRun { get; set; }
}
