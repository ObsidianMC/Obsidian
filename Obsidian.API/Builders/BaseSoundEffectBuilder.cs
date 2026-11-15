using Obsidian.API.Builders.Interfaces;

namespace Obsidian.API.Builders;
/// <summary>
/// Stores the playback settings shared by sound effect builders.
/// </summary>
public abstract class BaseSoundEffectBuilder : ISoundEffectBuilder, ISoundEffectBaseBuilder, IRangedSoundEffectBuilder
{
    private const float MinPitch = 2.0f;
    private const float MinVolume = 1.0f;

    /// <summary>
    /// Gets the namespaced sound event identifier to play.
    /// </summary>
    protected string SoundLocation { get; init; }

    /// <summary>
    /// Gets the client sound category used for volume control.
    /// </summary>
    protected SoundCategory SoundCategory { get; init; }

    /// <summary>
    /// Gets the optional sound name carried by the resulting effect.
    /// </summary>
    protected string? SoundName { get; init; }

    /// <summary>
    /// Gets or sets the optional playback position.
    /// </summary>
    protected SoundPosition? SoundPosition { get; set; }

    /// <summary>
    /// Gets or sets the optional fixed audible range in blocks.
    /// </summary>
    protected float? FixedRange { get; set; }

    /// <summary>
    /// Gets or sets the playback volume multiplier.
    /// </summary>
    protected float Volume { get; set; } = .5f;

    /// <summary>
    /// Gets or sets the playback pitch multiplier.
    /// </summary>
    protected float Pitch { get; set; } = .5f;

    /// <summary>
    /// Gets or sets the seed used for the client's sound variation.
    /// </summary>
    protected long Seed { get; set; }

    /// <summary>
    /// Gets or sets the optional entity ID to which playback is attached.
    /// </summary>
    protected int? EntityId { get; set; }

    /// <inheritdoc/>
    public virtual IRangedSoundEffectBuilder WithFixedRange(float range)
    {
        this.FixedRange = range;

        return this;
    }


    /// <inheritdoc/>
    public virtual ISoundEffectBaseBuilder WithPitch(float pitch)
    {
        this.Pitch = Math.Min(pitch, MinPitch);

        return this;
    }

    /// <inheritdoc/>
    public virtual ISoundEffectBaseBuilder WithSeed(long seed)
    {
        this.Seed = seed;

        return this;
    }

    /// <inheritdoc/>
    public virtual ISoundEffectBaseBuilder WithSoundPosition(SoundPosition soundPosition)
    {
        this.SoundPosition = soundPosition;

        return this;
    }

    /// <inheritdoc/>
    public virtual ISoundEffectBaseBuilder WithEntityId(int entityId)
    {
        this.EntityId = entityId;

        return this;
    }

    /// <inheritdoc/>
    public virtual ISoundEffectBaseBuilder WithVolume(float volume)
    {
        this.Volume = Math.Min(volume, MinVolume);

        return this;
    }

    /// <inheritdoc/>
    public abstract ISoundEffect Build();
}
