namespace Obsidian.API;
[Flags]
public enum PlayerInput : byte
{
    Forward = 0x01,
    Backward = 0x02,
    Left = 0x04,
    Right = 0x08,
    Jump = 0x10,
    Sneak = 0x20,
    Sprint = 0x40
}
