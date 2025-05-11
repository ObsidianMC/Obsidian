using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfVariant;
public sealed class WolfVariantElement : INbtSerializable
{
    public Dictionary<string, string> Assets { get; set; } = [];

    public List<WolfVariantSpawnCondition> SpawnConditions { get; set; } = [];

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("assets");

        foreach (var asset in this.Assets)
            writer.WriteString(asset.Key, asset.Value);

        writer.EndCompound();

        writer.WriteListStart("spawn_conditions", NbtTagType.Compound, this.SpawnConditions.Count);

        foreach(var spawnCondition in this.SpawnConditions)
        {
            writer.WriteCompoundStart();

            spawnCondition.Write(writer);

            writer.EndCompound();
        }

        writer.EndList();
    }
}

public sealed class WolfVariantSpawnCondition : INbtSerializable
{
    public SpawnCondition? Condition { get; set; }

    public required int Priority { get; set; }

    public void Write(INbtWriter writer)
    {
        if (this.Condition is SpawnCondition condition)
        {
            writer.WriteCompoundStart("condition");

            writer.WriteString("type", condition.Type);
            writer.WriteString("biomes", condition.Biomes);

            writer.EndCompound();
        }
        
        writer.WriteInt("priority", this.Priority);
    }

    public sealed class SpawnCondition
    {
        public required string Type { get; set; }

        public required string Biomes { get; set; }
    }
}
