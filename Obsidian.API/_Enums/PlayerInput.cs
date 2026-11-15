namespace Obsidian.API;
/// <summary>The movement keys a client reports as held.</summary>
[Flags]
public enum PlayerInput : byte
{
    /// <summary>Moving forward.</summary>
    Forward = 0x01,
    /// <summary>Moving backward.</summary>
    Backward = 0x02,
    /// <summary>Strafing left.</summary>
    Left = 0x04,
    /// <summary>Strafing right.</summary>
    Right = 0x08,
    /// <summary>Jumping.</summary>
    Jump = 0x10,
    /// <summary>Sneaking.</summary>
    Sneak = 0x20,
    /// <summary>Sprinting.</summary>
    Sprint = 0x40
}
