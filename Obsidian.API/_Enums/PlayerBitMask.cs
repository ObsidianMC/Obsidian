namespace Obsidian.API;

/// <summary>The skin layers a player shows, sent in player metadata.</summary>
[Flags]
public enum PlayerBitMask : byte
{
    /// <summary>Not used by the client.</summary>
    Unused = 0x80,

    /// <summary>The cape is shown.</summary>
    CapeEnabled = 0x01,
    /// <summary>The jacket layer is shown.</summary>
    JacketEnabled = 0x02,

    /// <summary>The left sleeve layer is shown.</summary>
    LeftSleeveEnabled = 0x04,
    /// <summary>The right sleeve layer is shown.</summary>
    RightSleeveEnabled = 0x08,

    /// <summary>The left trouser leg layer is shown.</summary>
    LeftPantsLegEnabled = 0x10,
    /// <summary>The right trouser leg layer is shown.</summary>
    RIghtPantsLegEnabled = 0x20,

    /// <summary>The hat layer is shown.</summary>
    HatEnabled = 0x40
}
