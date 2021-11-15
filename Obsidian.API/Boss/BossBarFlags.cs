namespace Obsidian.API.Boss;

[Flags]
public enum BossBarFlags : byte
{
    None = 0x0,
    DarkenSky = 0x1,
    DragonBar = 0x2,
    CreateFog = 0x04
}
