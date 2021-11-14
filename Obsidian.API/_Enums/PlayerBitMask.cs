using System;

namespace Obsidian.API;

[Flags]
public enum PlayerBitMask : byte
{
    Unused = 0x80,

    CapeEnabled = 0x01,
    JacketEnabled = 0x02,

    LeftSleeveEnabled = 0x04,
    RightSleeveEnabled = 0x08,

    LeftPantsLegEnabled = 0x10,
    RIghtPantsLegEnabled = 0x20,

    HatEnabled = 0x40
}
