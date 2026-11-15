using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfSoundVariant;
/// <summary>
/// Represents a wolf sound variant registry entry and its synchronized element data.
/// </summary>
public sealed record class WolfSoundVariantCodec : ICodec
{
    /// <summary>
    /// Gets the entry's namespaced registry identifier.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the numeric registry ID used on the network.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the wolf sound variant data serialized for this entry.
    /// </summary>
    public required WolfSoundVariantElement Element { get; init; }

    /// <summary>
    /// Writes this entry's wolf sound variant fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        this.Element.AdultSounds.Write(writer, "adult_sounds");
        this.Element.BabySounds.Write(writer, "baby_sounds");
    }
}
