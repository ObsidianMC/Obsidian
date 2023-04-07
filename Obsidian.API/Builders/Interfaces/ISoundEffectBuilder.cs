namespace Obsidian.API.Builders.Interfaces;
public interface ISoundEffectBuilder
{
    public ISoundEffectBaseBuilder WithSoundPosition(SoundPosition soundPosition);

    public ISoundEffectBaseBuilder WithEntityId(int id);
}

public interface ISoundEffectBaseBuilder
{
    public IRangedSoundEffectBuilder WithFixedRange();

    public ISoundEffectBaseBuilder WithVolume(float volume);

    public ISoundEffectBaseBuilder WithPitch(float pitch);

    public ISoundEffectBaseBuilder WithSeed(long seed);

    public ISoundEffect Build();
}

public interface IRangedSoundEffectBuilder
{
    public ISoundEffectBaseBuilder WithRange(float range);
}
