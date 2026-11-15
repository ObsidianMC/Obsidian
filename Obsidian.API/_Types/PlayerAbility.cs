namespace Obsidian.API;

/// <summary>The abilities a player has, sent to their client.</summary>
[Flags]
public enum PlayerAbility : sbyte
{
    /// <summary>No abilities.</summary>
    None = 0x00,
    /// <summary>The player takes no damage.</summary>
    Invulnerable = 0x01,
    /// <summary>The player is flying.</summary>
    Flying = 0x02,
    /// <summary>The player can start flying.</summary>
    AllowFlying = 0x04,
    /// <summary>The player breaks blocks instantly, as in creative mode.</summary>
    CreativeMode = 0x08
}
