using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfSoundVariant;
public sealed record class WolfSoundVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required WolfSoundVariantElement Element { get; init; }

    public void WriteElement(INbtWriter writer)
    {
        this.Element.AdultSounds.Write(writer, "adult_sounds");
        this.Element.BabySounds.Write(writer, "baby_sounds");
    }
}
