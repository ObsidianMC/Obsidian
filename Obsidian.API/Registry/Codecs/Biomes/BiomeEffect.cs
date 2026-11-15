using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

/// <summary>
/// A 1.21.11 biome's effects (vanilla's BiomeSpecialEffects). Colours are hex strings like <c>#3f76e4</c>, as vanilla
/// writes them; sky and fog colours, particles and sounds became environment attributes in 1.21.11.
/// </summary>
public sealed record class BiomeEffect : INbtSerializable
{
    /// <summary>
    /// Gets or sets the water tint as a hexadecimal color string, serialized as <c>water_color</c>.
    /// </summary>
    public required string WaterColor { get; set; }

    /// <summary>
    /// Gets or sets the optional foliage tint; <c>null</c> omits <c>foliage_color</c>.
    /// </summary>
    public string? FoliageColor { get; set; }

    /// <summary>
    /// Gets or sets the optional dry foliage tint; <c>null</c> omits <c>dry_foliage_color</c>.
    /// </summary>
    public string? DryFoliageColor { get; set; }

    /// <summary>
    /// Gets or sets the optional grass tint; <c>null</c> omits <c>grass_color</c>.
    /// </summary>
    public string? GrassColor { get; set; }

    /// <summary>
    /// <c>dark_forest</c> or <c>swamp</c>, or <c>none</c> or null for none.
    /// </summary>
    public string? GrassColorModifier { get; set; }

    // Matches vanilla's BiomeSpecialEffects codec: absent optional colours and a "none" modifier are left out.
    /// <summary>
    /// Writes the <c>effects</c> compound, omitting absent colors and a missing or <c>none</c> grass modifier.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("effects");

        writer.WriteString("water_color", this.WaterColor);

        if (this.FoliageColor is not null)
            writer.WriteString("foliage_color", this.FoliageColor);

        if (this.DryFoliageColor is not null)
            writer.WriteString("dry_foliage_color", this.DryFoliageColor);

        if (this.GrassColor is not null)
            writer.WriteString("grass_color", this.GrassColor);

        if (this.GrassColorModifier is not null and not "none")
            writer.WriteString("grass_color_modifier", this.GrassColorModifier);

        writer.EndCompound();
    }
}
