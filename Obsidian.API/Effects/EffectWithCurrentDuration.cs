namespace Obsidian.API.Effects;
/// <summary>A potion effect active on a living entity, with the time it has left.</summary>
public sealed record class EffectWithCurrentDuration
{
    /// <summary>Gets the effect as it was applied, including its full duration.</summary>
    public required PotionEffectData EffectData { get; init; }

    /// <summary>Gets or sets the ticks left before the effect ends, or -1 if it never runs out.</summary>
    public required int CurrentDuration { get; set; }

}
