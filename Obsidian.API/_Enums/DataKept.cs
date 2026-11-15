namespace Obsidian.API;
/// <summary>Which player data the client keeps when it respawns or changes dimension.</summary>
public enum DataKept : sbyte
{
    /// <summary>The client resets everything.</summary>
    None,

    /// <summary>The client keeps the player's attributes.</summary>
    Attributes = 0x01,

    /// <summary>The client keeps the player's entity metadata.</summary>
    Metadata = 0x02
}
