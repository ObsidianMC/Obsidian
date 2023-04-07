namespace Obsidian.API;
public interface ISoundEffect
{
    public SoundId SoundId { get; init; }

    /// <summary>
    /// The name of the sound you want to use.
    /// </summary>
    /// <remarks>
    /// Present if <see cref="SoundId"/> is <see cref="SoundId.None"/>.
    /// </remarks>
    public string? SoundName { get; init; }

    /// <remarks>
    /// Present if <see cref="SoundName"/> has a value.
    /// </remarks>
    public bool? HasFixedRange { get; init; }

    /// <summary>
    /// The fixed range of the sound.
    /// </summary>
    /// <remarks>
    /// Has value if <see cref="HasFixedRange"/> is true.
    /// </remarks>
    public float? Range { get; init; }

    /// <summary>
    /// The category that this sound will be played from.
    /// </summary>
    public SoundCategory SoundCategory { get; init; }

    /// <summary>
    /// The position of where the sound originated from.
    /// </summary>
    /// <remarks>
    ///  Null if <see cref="EntityId"/> has a value.
    /// </remarks>
    public SoundPosition? SoundPosition { get; init; }

    /// <summary>
    /// The entity that the sound originated from.
    /// </summary>
    /// <remarks>
    ///  Null if <see cref="SoundPosition"/> has a value.
    /// </remarks>
    public int? EntityId { get; init; }

    /// <remarks>
    /// Must be a value between 0.0 and 1.0
    /// </remarks>
    public float Volume { get; init; }

    /// <remarks>
    /// Must be a value between 0.5 and 2.0
    /// </remarks>
    public float Pitch { get; init; }

    /// <summary>
    /// Seed used to pick sound variant.
    /// </summary>
    public float Seed { get; init; }
}
