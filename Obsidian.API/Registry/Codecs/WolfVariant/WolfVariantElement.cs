using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfVariant;
/// <summary>
/// Contains adult and puppy textures and spawn conditions for a wolf appearance variant.
/// </summary>
public sealed class WolfVariantElement : INbtSerializable
{
    /// <summary>
    /// Gets or sets the adult texture identifiers keyed by state, such as <c>angry</c>, <c>tame</c> and <c>wild</c>.
    /// </summary>
    public Dictionary<string, string> Assets { get; set; } = [];

    /// <summary>The textures of the variant's puppies, by state like <see cref="Assets"/>.</summary>
    public Dictionary<string, string> BabyAssets { get; set; } = [];

    /// <summary>
    /// Gets or sets the prioritized variant selection conditions serialized as <c>spawn_conditions</c>.
    /// </summary>
    public List<SpawnConditionElement> SpawnConditions { get; set; } = [];

    /// <summary>
    /// Writes adult assets, baby assets and spawn conditions as variant NBT fields.
    /// </summary>
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
