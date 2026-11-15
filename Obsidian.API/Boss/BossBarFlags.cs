namespace Obsidian.API.Boss;

/// <summary>Extra client-side effects a boss bar turns on while players can see it.</summary>
[Flags]
public enum BossBarFlags : byte
{
    /// <summary>No extra effects.</summary>
    None = 0x0,
    /// <summary>Darkens the sky for players who see the bar.</summary>
    DarkenSky = 0x1,
    /// <summary>Marks the bar as the ender dragon's, which plays the End boss music.</summary>
    DragonBar = 0x2,
    /// <summary>Draws thick fog around players who see the bar.</summary>
    CreateFog = 0x04
}
