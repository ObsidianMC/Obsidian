using Obsidian.API.World.DimensionSettings;

namespace Obsidian.API.World;

public sealed class WorldGenSetting
{
    public bool BonusChest { get; set; }
    public bool GenerateFeatures { get; set; }

    public long Seed { get; set; }

    public Dictionary<string, DimensionSetting> Dimensions { get; set; }
}
