using Obsidian.API.Utilities;
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

    public void WriteElement(INbtWriter writer)
    {
        writer.WriteBool("has_precipitation", this.Element.HasPrecipitation);
        writer.WriteFloat("depth", this.Element.Depth);
        writer.WriteFloat("temperature", this.Element.Temperature);
        writer.WriteFloat("scale", this.Element.Scale);
        writer.WriteFloat("downfall", this.Element.Downfall);

        if (!this.Element.Category.IsNullOrEmpty())
            writer.WriteString("category", this.Element.Category!);

        this.Element.Effects.Write(writer);

        if (!this.Element.TemperatureModifier.IsNullOrEmpty())
            writer.WriteString("temperature_modifier", this.Element.TemperatureModifier);
    }
}
