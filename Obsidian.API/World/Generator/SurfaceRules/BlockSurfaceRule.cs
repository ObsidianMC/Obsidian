namespace Obsidian.API.World.Generator.SurfaceRules;

/// <summary>
/// Produces one configured block state for a surface position.
/// </summary>
[SurfaceRule("minecraft:block")]
public sealed class BlockSurfaceRule : ISurfaceRule
{
    /// <summary>
    /// Gets the surface rule codec type identifier.
    /// </summary>
    public string Type => "minecraft:block";

    /// <summary>
    /// Gets the block state produced by the rule, read from <c>result_state</c>.
    /// </summary>
    public required SimpleBlockState ResultState { get; init; }
}
