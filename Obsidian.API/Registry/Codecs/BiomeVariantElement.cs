using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
public sealed record class BiomeVariantElement : INbtSerializable
{
    public required string AssetId { get; set; }

    /// <summary>The texture of the variant's babies, for the mobs whose babies have their own (cats, pigs, ...).</summary>
    public string? BabyAssetId { get; set; }

    public string? Model { get; set; }

    public List<SpawnConditionElement> SpawnConditions { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteString("asset_id", this.AssetId);

        if (!string.IsNullOrEmpty(this.BabyAssetId))
            writer.WriteString("baby_asset_id", this.BabyAssetId);

        if(!string.IsNullOrEmpty(this.Model))
            writer.WriteString("model", this.Model);

        writer.WriteListStart("spawn_conditions", NbtTagType.Compound, this.SpawnConditions.Count);

        foreach (var spawnCondition in this.SpawnConditions)
        {
            writer.WriteCompoundStart();

            spawnCondition.Write(writer);

            writer.EndCompound();
        }

        writer.EndList();
    }
}
