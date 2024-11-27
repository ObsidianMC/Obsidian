namespace Obsidian.API;

/// <summary>
/// The potion effect data holds additional information about an active <see cref="PotionEffect"/>.
/// </summary>
public class PotionEffectData
{
    /// <summary>
    /// The duration of the potion effect when it was added.
    /// </summary>
    public int Duration { get; }

    /// <summary>
    /// The amplifier of the potion effect.
    /// </summary>
    public int Amplifier { get; }

    /// <summary>
    /// The flags which define some settings with the potion effect itself.
    /// See https://wiki.vg/Protocol#Entity_Effect for more information.
    /// </summary>
    public byte Flags { get; }

    /// <summary>
    /// The current duration of the potion effect, how many ticks the potion effect will still last.
    /// </summary>
    public int CurrentDuration { get; set; }

    public PotionEffectData(int duration, int amplifier, byte flags)
    {
        Duration = duration;
        CurrentDuration = duration;
        Amplifier = amplifier;
        Flags = flags;
    }
}
