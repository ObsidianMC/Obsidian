namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionSetting
{
    public string Type { get; set; }

    public DimensionGeneratorSettings Generator { get; set; }
}
