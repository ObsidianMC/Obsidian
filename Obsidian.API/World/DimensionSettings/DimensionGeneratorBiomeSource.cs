namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionGeneratorBiomeSource
{
    public string Type { get; set; }

    public string? Preset { get; set; }

    public string? Biome { get; set; }

    public List<DimensionGeneratorBiomeEntry>? Biomes { get; set; }
}
