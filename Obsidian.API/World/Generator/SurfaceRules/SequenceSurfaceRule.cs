namespace Obsidian.API.World.Generator.SurfaceRules;

/// <summary>
/// Tries surface rules in order and uses the first block result.
/// </summary>
[SurfaceRule("minecraft:sequence")]
public sealed class SequenceSurfaceRule : ISurfaceRule
{
    /// <summary>
    /// Gets the surface rule codec type identifier.
    /// </summary>
    public string Type => "minecraft:sequence";

    /// <summary>
    /// Gets the surface rules in evaluation order.
    /// </summary>
    public required ImmutableArray<ISurfaceRule> Sequence { get; init; }
}
