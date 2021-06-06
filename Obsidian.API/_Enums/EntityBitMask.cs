using System;

namespace Obsidian.API
{
    [Flags]
    public enum EntityBitMask : byte
    {
        None = 0x00,
        OnFire = 0x01,
        Crouched = 0x02,

        [Obsolete]
        Riding = 0x04,

        Sprinting = 0x08,
        Swimming = 0x10,
        Invisible = 0x20,
        Glowing = 0x40,
        FlyingWithElytra = 0x80
    }
}
