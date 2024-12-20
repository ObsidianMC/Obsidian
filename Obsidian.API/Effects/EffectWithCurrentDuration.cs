namespace Obsidian.API.Effects;
public sealed record class EffectWithCurrentDuration
{
    public required PotionEffectData EffectData { get; init; }

    public required int CurrentDuration { get; set; }

}
