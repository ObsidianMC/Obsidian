namespace Obsidian.API.World.Generator.SurfaceRules;

[SurfaceRule("minecraft:condition")]
public sealed class ConditionSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:condition";

    public required ISurfaceCondition IfTrue { get; init; }

    public required ISurfaceRule ThenRun { get; init; }
}
