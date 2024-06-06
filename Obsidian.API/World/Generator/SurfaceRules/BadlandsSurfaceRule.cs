namespace Obsidian.API.World.Generator.SurfaceRules;

[SurfaceRule("minecraft:badlands")]
public sealed class BadlandsSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:badlands";
}
