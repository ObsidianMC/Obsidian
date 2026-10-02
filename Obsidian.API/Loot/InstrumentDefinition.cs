namespace Obsidian.API.Loot;

/// <summary>
/// A vanilla instrument (the sound a goat horn plays). The vanilla instruments are in
/// <see cref="Registries.InstrumentsRegistry"/>.
/// </summary>
public sealed class InstrumentDefinition
{
    /// <summary>
    /// Registry id, e.g. <c>minecraft:ponder_goat_horn</c>.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Index in vanilla's instrument registry, which is sorted by id; the id clients know the instrument by.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// The sound event id, e.g. <c>minecraft:item.goat_horn.sound.0</c>.
    /// </summary>
    public required string SoundEvent { get; init; }

    /// <summary>
    /// How long the instrument plays, in seconds.
    /// </summary>
    public required float UseDuration { get; init; }

    /// <summary>
    /// How far the sound can be heard, in blocks.
    /// </summary>
    public required float Range { get; init; }

    public required ChatMessage Description { get; init; }

    /// <summary>
    /// The instrument as the value of an item's <c>minecraft:instrument</c> component.
    /// </summary>
    public InstrumentData ToInstrumentData() => new()
    {
        Identifier = this.Identifier,
        SoundEvent = new SoundEvent { ResourceLocation = this.SoundEvent },
        UseDuration = this.UseDuration,
        Range = this.Range,
        Description = this.Description with { }
    };
}
