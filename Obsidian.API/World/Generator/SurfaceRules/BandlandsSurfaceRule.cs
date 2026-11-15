namespace Obsidian.API.World.Generator.SurfaceRules;

/// <summary>
/// Describes the banded terracotta surface rule identified by <c>minecraft:bandlands</c>.
/// </summary>
[SurfaceRule("minecraft:bandlands")]
public sealed class BandlandsSurfaceRule : ISurfaceRule
{
    /// <summary>
    /// Gets the surface rule codec type identifier.
    /// </summary>
    public string Type => "minecraft:bandlands";
}
