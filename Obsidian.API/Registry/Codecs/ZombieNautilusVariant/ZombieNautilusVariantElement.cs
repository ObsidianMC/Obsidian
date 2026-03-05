using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ZombieNautilusVariant;

public sealed record class ZombieNautilusVariantElement : INbtSerializable
{
    public string AssetId { get; set; }

    public string? Model { get; set; }

    public SpawnConditionElement[] SpawnConditions { get; set; }

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
                condition.Write(writer);
            }

            writer.EndList();
        }
    }
}
