using Obsidian.API.Builders.Interfaces;

namespace Obsidian.API.Builders;
public sealed class SoundEffectBuilder : BaseSoundEffectBuilder
{
    internal SoundEffectBuilder() { }

    /// <summary>Builds a playable effect from a registered or inline event, preserving its fixed range.</summary>
    public static ISoundEffectBuilder Create(SoundEvent sound, SoundCategory soundCategory = SoundCategory.Master) =>
        new SoundEffectBuilder
        {
            SoundLocation = sound.ResourceLocation,
            FixedRange = sound.FixedRange,
            SoundCategory = soundCategory
        };

    public static ISoundEffectBuilder Create(string soundLocation, SoundCategory soundCategory = SoundCategory.Master) => new SoundEffectBuilder
    {
        SoundLocation = soundLocation,
        SoundCategory = soundCategory
    };

    public override ISoundEffect Build() => new SoundEffect
    {
        SoundId = this.SoundLocation,
        SoundCategory = this.SoundCategory,
        SoundPosition = this.SoundPosition,
        Volume = this.Volume,
        Pitch = this.Pitch,
        Seed = this.Seed,
        SoundName = this.SoundName,
        FixedRange = this.FixedRange,
        EntityId = this.EntityId
    };
}
