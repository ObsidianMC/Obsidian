using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ZombieNautilusVariant;

/// <summary>
/// Contains a variant's client asset, optional model and spawn condition entries.
/// </summary>
public sealed record class ZombieNautilusVariantElement : INbtSerializable
{
    /// <summary>
    /// Gets or sets the client variant asset identifier, serialized as <c>asset_id</c>.
    /// </summary>
    public required string AssetId { get; set; }

    /// <summary>
    /// Gets or sets the optional model identifier; omitted from NBT when absent or empty.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the prioritized spawn conditions serialized as <c>spawn_conditions</c>.
    /// </summary>
    public required ImmutableArray<SpawnConditionElement> SpawnConditions { get; set; }

    /// <summary>
    /// Writes the variant's asset, optional model and spawn condition fields to NBT.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        writer.WriteString("asset_id", this.AssetId);

        if(!this.Model.IsNullOrWhitespace())
        {
            writer.WriteString("model", this.Model);
        }

        if (this.SpawnConditions.Length > 0)
        {
            writer.WriteListStart("spawn_conditions", NbtTagType.Compound, this.SpawnConditions.Length);

            foreach (var condition in this.SpawnConditions)
            {
                writer.WriteCompoundStart();

                condition.Write(writer);

                writer.EndCompound();
            }

            writer.EndList();
        }
    }
}
