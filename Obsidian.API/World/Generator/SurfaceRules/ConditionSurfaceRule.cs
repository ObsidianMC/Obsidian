namespace Obsidian.API.World.Generator.SurfaceRules;

/// <summary>
/// Applies a nested surface rule only when its condition succeeds.
/// </summary>
[SurfaceRule("minecraft:condition")]
public sealed class ConditionSurfaceRule : ISurfaceRule
{
    /// <summary>
    /// Gets the surface rule codec type identifier.
    /// </summary>
    public string Type => "minecraft:condition";

    /// <summary>
    /// Gets the condition tested before applying <see cref="ThenRun"/>.
    /// </summary>
    public required ISurfaceCondition IfTrue { get; init; }

    /// <summary>
    /// Gets the rule applied when <see cref="IfTrue"/> succeeds.
    /// </summary>
    public required ISurfaceRule ThenRun { get; init; }
}
