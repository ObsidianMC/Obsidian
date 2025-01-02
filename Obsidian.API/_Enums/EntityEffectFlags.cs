namespace Obsidian.API;
[Flags]
public enum EntityEffectFlags : sbyte
{
    None = 0x00,

    /// <summary>
    /// Whether the potion is emitted by ambient source e.g. the beacon. The icon has a blue border in the HUD if its ambient.
    /// </summary>
    IsAmbient = 0x01,

    /// <summary>
    /// Whether to show the particles or not.
    /// </summary>
    ShowParticles = 0x02,

    /// <summary>
    /// Whether to show the icon on the client or not.
    /// </summary>
    ShowIcon = 0x04,
    Blend = 0x08
}
