namespace Obsidian.API.Registry.Codecs.WolfVariant;
public sealed class WolfVariantElement
{
    public Dictionary<string, string> Assets { get; set; } = [];

    public List<WolfVariantSpawnCondition> SpawnConditions { get; set; } = [];
}

public sealed class WolfVariantSpawnCondition
{
    public SpawnCondition? Condition { get; set; }

    public required int Priority { get; set; }

    public sealed class SpawnCondition
    {
        public required string Type { get; set; }

        public required string Biomes { get; set; }
    }
}
