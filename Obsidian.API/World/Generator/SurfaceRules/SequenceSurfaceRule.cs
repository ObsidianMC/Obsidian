namespace Obsidian.API.World.Generator.SurfaceRules;
public sealed class SequenceSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:sequence";

    public ISurfaceRule[] Sequence { get; set; }
}
