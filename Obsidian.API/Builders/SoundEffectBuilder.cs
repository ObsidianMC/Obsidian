using Obsidian.API.Builders.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Builders;
public sealed class SoundEffectBuilder : BaseSoundEffectBuilder
{
    internal SoundEffectBuilder() { }

    public static ISoundEffectBuilder Create(SoundId soundId, SoundCategory soundCategory = SoundCategory.Master) => new SoundEffectBuilder
    {
        SoundId = soundId,
        SoundCategory = soundCategory
    };

    public static ISoundEffectBuilder Create([NotNull] string soundName, SoundCategory soundCategory = SoundCategory.Master)
    {
        ArgumentException.ThrowIfNullOrEmpty(soundName);

        return new SoundEffectBuilder
        {
            SoundName = soundName,
            SoundId = SoundId.None,
            SoundCategory = soundCategory
        };
    }

    public override ISoundEffect Build() => new SoundEffect
    {
        SoundId = this.SoundId,
        SoundCategory = this.SoundCategory,
        SoundPosition = this.SoundPosition,
        Volume = this.Volume,
        Pitch = this.Pitch,
        Seed = this.Seed,
        SoundName = this.SoundName,
        HasFixedRange = this.HasFixedRange,
        Range = this.Range,
        EntityId = this.EntityId
    };
}
