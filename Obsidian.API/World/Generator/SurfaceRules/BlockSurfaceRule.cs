namespace Obsidian.API.World.Generator.SurfaceRules;

[SurfaceRule("minecraft:block")]
public sealed class BlockSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:block";

    public required SimpleBlockState ResultState { get; init; }
}
