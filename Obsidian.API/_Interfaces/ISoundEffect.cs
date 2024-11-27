namespace Obsidian.API;
public interface ISoundEffect
{
    public string SoundId { get; init; }

    /// <summary>
    /// The fixed range of the sound.
    /// </summary>
    public float? FixedRange { get; init; }

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
    /// The entity id that the sound originated from.
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
    public long Seed { get; init; }
}
