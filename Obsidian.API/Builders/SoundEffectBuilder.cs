using Obsidian.API.Builders.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Builders;
public sealed class SoundEffectBuilder : BaseSoundEffectBuilder
{
    internal SoundEffectBuilder() { }

    public static ISoundEffectBuilder Create(SoundId soundId, SoundCategory soundCategory) => new SoundEffectBuilder
    {
        SoundId = soundId,
        SoundCategory = soundCategory
    };

    public static ISoundEffectBuilder Create([NotNull] string soundName, SoundCategory soundCategory)
    {
        ArgumentException.ThrowIfNullOrEmpty(soundName);

        return new SoundEffectBuilder
        {
            SoundName = soundName,
            SoundId = SoundId.None,
            SoundCategory = soundCategory
        };
    }

    public override ISoundEffect Build()
    {
        if (this.EntityId.HasValue)
            return new EntitySoundEffect
            {
                SoundId = this.SoundId,
                SoundCategory = this.SoundCategory,
                EntityId = this.EntityId.Value,
                Volume = this.Volume,
                Pitch = this.Pitch,
                Seed = this.Seed,
                SoundName = this.SoundName,
                HasFixedRange = this.HasFixedRange,
                Range = this.Range
            };

        return new SoundEffect
        {
            SoundId = this.SoundId,
            SoundCategory = this.SoundCategory,
            SoundPosition = this.SoundPosition,
            Volume = this.Volume,
            Pitch = this.Pitch,
            Seed = this.Seed,
            SoundName = this.SoundName,
            HasFixedRange = this.HasFixedRange,
            Range = this.Range
        };
    }
}
