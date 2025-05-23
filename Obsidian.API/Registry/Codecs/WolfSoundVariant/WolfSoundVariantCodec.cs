using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfSoundVariant;
public sealed record class WolfSoundVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required WolfSoundVariantElement Element { get; init; }

    public void WriteElement(INbtWriter writer)
    {
        writer.WriteString("ambient_sound", this.Element.AmbientSound);
        writer.WriteString("death_sound", this.Element.DeathSound);
        writer.WriteString("growl_sound", this.Element.GrowlSound);
        writer.WriteString("hurt_sound", this.Element.HurtSound);
        writer.WriteString("pant_sound", this.Element.PantSound);
        writer.WriteString("whine_sound", this.Element.WhineSound);
    }
}
