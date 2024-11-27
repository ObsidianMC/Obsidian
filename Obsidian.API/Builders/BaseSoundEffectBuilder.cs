using Obsidian.API.Builders.Interfaces;

namespace Obsidian.API.Builders;
public abstract class BaseSoundEffectBuilder : ISoundEffectBuilder, ISoundEffectBaseBuilder, IRangedSoundEffectBuilder
{
    private const float MinPitch = 2.0f;
    private const float MinVolume = 1.0f;

    protected string SoundLocation { get; init; }

    protected SoundCategory SoundCategory { get; init; }

    protected string? SoundName { get; init; }

    protected SoundPosition? SoundPosition { get; set; }

    protected float? FixedRange { get; set; }

    protected float Volume { get; set; } = .5f;

    protected float Pitch { get; set; } = .5f;

    protected long Seed { get; set; }

    protected int? EntityId { get; set; }

    public virtual IRangedSoundEffectBuilder WithFixedRange(float range)
    {
        this.FixedRange = range;

        return this;
    }


    public virtual ISoundEffectBaseBuilder WithPitch(float pitch)
    {
        this.Pitch = Math.Min(pitch, MinPitch);

        return this;
    }

    public virtual ISoundEffectBaseBuilder WithSeed(long seed)
    {
        this.Seed = seed;

        return this;
    }

    public virtual ISoundEffectBaseBuilder WithSoundPosition(SoundPosition soundPosition)
    {
        this.SoundPosition = soundPosition;

        return this;
    }

    public virtual ISoundEffectBaseBuilder WithEntityId(int entityId)
    {
        this.EntityId = entityId;

        return this;
    }

    public virtual ISoundEffectBaseBuilder WithVolume(float volume)
    {
        this.Volume = Math.Min(volume, MinVolume);

        return this;
    }

    public abstract ISoundEffect Build();
}
