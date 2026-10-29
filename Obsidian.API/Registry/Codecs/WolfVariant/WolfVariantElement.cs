using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfVariant;
public sealed class WolfVariantElement : INbtSerializable
{
    public Dictionary<string, string> Assets { get; set; } = [];

    /// <summary>The textures of the variant's puppies, by state like <see cref="Assets"/>.</summary>
    public Dictionary<string, string> BabyAssets { get; set; } = [];

    public List<SpawnConditionElement> SpawnConditions { get; set; } = [];

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("assets");

        foreach (var asset in this.Assets)
            writer.WriteString(asset.Key, asset.Value);

        writer.EndCompound();

        writer.WriteCompoundStart("baby_assets");

        foreach (var asset in this.BabyAssets)
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
