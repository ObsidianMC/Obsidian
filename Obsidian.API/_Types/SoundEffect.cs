namespace Obsidian.API;
/// <summary>A sound to play, built with a sound effect builder.</summary>
public sealed class SoundEffect : ISoundEffect
{
    /// <summary>Gets the sound event's id, such as <c>minecraft:block.note_block.harp</c>.</summary>
    public required string SoundId { get; init; }
    /// <inheritdoc/>
    public required SoundCategory SoundCategory { get; init; }

    /// <summary>Gets the volume. The builder caps it at 1 but sets no lower bound.</summary>
    public required float Volume { get; init; }
    /// <summary>Gets the pitch multiplier. The builder caps it at 2 but sets no lower bound.</summary>
    public required float Pitch { get; init; }
    /// <inheritdoc/>
    public required long Seed { get; init; }

    /// <inheritdoc/>
    public float? FixedRange { get; init; }

    /// <summary>
    /// Gets a sound event name that is written instead of <see cref="SoundId"/> when the sound is sent by name, or null
    /// to use <see cref="SoundId"/>.
    /// </summary>
    public string? SoundName { get; init; }
    /// <inheritdoc/>
    public int? EntityId { get; init; }

    /// <inheritdoc/>
    public SoundPosition? SoundPosition { get; init; }

    internal SoundEffect() { }
}
