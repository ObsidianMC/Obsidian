using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
/// <summary>
/// Contains a variant's client asset, optional model and spawn condition entries.
/// </summary>
public sealed record class BiomeVariantElement : INbtSerializable
{
    /// <summary>
    /// Gets or sets the client variant asset identifier, serialized as <c>asset_id</c>.
    /// </summary>
    public required string AssetId { get; set; }

    /// <summary>The texture of the variant's babies, for the mobs whose babies have their own (cats, pigs, ...).</summary>
    public string? BabyAssetId { get; set; }

    /// <summary>
    /// Gets or sets the optional model identifier; omitted from NBT when absent or empty.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the prioritized spawn conditions serialized as <c>spawn_conditions</c>.
    /// </summary>
    public List<SpawnConditionElement> SpawnConditions { get; set; }

    /// <summary>
    /// Writes the variant's asset, optional model and spawn condition fields to NBT.
    /// </summary>
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
