namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionGeneratorSettings
{
    public IDimensionSetting Settings { get; set; }
    public string Type { get; set; }

    public string? Dimension { get; set; }

    public DimensionGeneratorBiomeSource BiomeSource { get; set; }
}
