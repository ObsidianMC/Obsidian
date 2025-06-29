namespace Obsidian.API.World.Generator.SurfaceRules;

[SurfaceRule("minecraft:sequence")]
public sealed class SequenceSurfaceRule : ISurfaceRule
{
    public string Type => "minecraft:sequence";

    public required ISurfaceRule[] Sequence { get; init; }
}
