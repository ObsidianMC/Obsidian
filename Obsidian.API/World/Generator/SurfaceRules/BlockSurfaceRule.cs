namespace Obsidian.API.World.Generator.SurfaceRules;
public sealed class BlockSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:block";

    public SimpleBlockState ResultState { get; set; }
}
