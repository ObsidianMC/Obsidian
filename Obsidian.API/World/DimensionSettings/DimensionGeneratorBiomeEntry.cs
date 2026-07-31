namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionGeneratorBiomeEntry
{
    public string Biome { get; set; }

    public DimensionGeneratorBiomeParameters Parameters { get; set; }
}
