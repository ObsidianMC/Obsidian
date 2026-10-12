using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    /// <summary>
    /// The entry's data. A plugin's entry gets a new element when its plugin reloads, and keeps its instance and id.
    /// </summary>
    public required BiomeElement Element { get; set; }

    /// <summary>
    /// Writes the climate and effects as vanilla's Biome.NETWORK_CODEC does. Clients get vanilla's biomes by name, and
    /// plugins' with the data their files decode to, attributes included.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        writer.WriteBool("has_precipitation", this.Element.HasPrecipitation);
        writer.WriteFloat("temperature", this.Element.Temperature);

        if (this.Element.TemperatureModifier is not null and not "none")
            writer.WriteString("temperature_modifier", this.Element.TemperatureModifier);

        writer.WriteFloat("downfall", this.Element.Downfall);

        this.Element.Effects.Write(writer);
    }
}
