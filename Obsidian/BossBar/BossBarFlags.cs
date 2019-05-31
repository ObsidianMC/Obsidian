using System;

namespace Obsidian.BossBar
{
    [Flags]
    public enum BossBarFlags : byte
    {
        DarkenSky = 0x1,
        DragonBar = 0x2,
        CreateFog = 0x04
    }
}
