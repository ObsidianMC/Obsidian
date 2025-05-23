using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfVariant;
public sealed class WolfVariantElement : INbtSerializable
{
    public Dictionary<string, string> Assets { get; set; } = [];

    public List<SpawnConditionElement> SpawnConditions { get; set; } = [];

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
