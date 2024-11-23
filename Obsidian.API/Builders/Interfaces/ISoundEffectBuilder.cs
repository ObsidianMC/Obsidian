namespace Obsidian.API.Builders.Interfaces;
public interface ISoundEffectBuilder
{
    public ISoundEffectBaseBuilder WithSoundPosition(SoundPosition soundPosition);

    public ISoundEffectBaseBuilder WithEntityId(int id);
}

public interface ISoundEffectBaseBuilder
{
    public ISoundEffectBaseBuilder WithVolume(float volume);

    public ISoundEffectBaseBuilder WithPitch(float pitch);

    public ISoundEffectBaseBuilder WithSeed(long seed);

    public ISoundEffect Build();
}

public interface IRangedSoundEffectBuilder
{
    public IRangedSoundEffectBuilder WithFixedRange(float range);
}
