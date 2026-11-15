namespace Obsidian.API.Builders.Interfaces;
/// <summary>
/// Selects a sound's playback position or attached entity.
/// </summary>
public interface ISoundEffectBuilder
{
    /// <summary>
    /// Sets the sound's playback position and returns this builder.
    /// </summary>
    public ISoundEffectBaseBuilder WithSoundPosition(SoundPosition soundPosition);

    /// <summary>
    /// Sets the entity to which playback is attached and returns this builder.
    /// </summary>
    public ISoundEffectBaseBuilder WithEntityId(int id);
}

/// <summary>
/// Configures sound volume, pitch and variation before building an effect.
/// </summary>
public interface ISoundEffectBaseBuilder
{
    /// <summary>
    /// Sets the volume, capped at 1, and returns this builder.
    /// </summary>
    public ISoundEffectBaseBuilder WithVolume(float volume);

    /// <summary>
    /// Sets the pitch multiplier, capped at 2, and returns this builder.
    /// </summary>
    public ISoundEffectBaseBuilder WithPitch(float pitch);

    /// <summary>
    /// Sets the client sound-variation seed and returns this builder.
    /// </summary>
    public ISoundEffectBaseBuilder WithSeed(long seed);

    /// <summary>
    /// Creates a sound effect with the configured playback settings.
    /// </summary>
    public ISoundEffect Build();
}

/// <summary>
/// Configures an explicit audible range for a sound effect.
/// </summary>
public interface IRangedSoundEffectBuilder
{
    /// <summary>
    /// Sets the sound's fixed audible range in blocks and returns this builder.
    /// </summary>
    public IRangedSoundEffectBuilder WithFixedRange(float range);
}
