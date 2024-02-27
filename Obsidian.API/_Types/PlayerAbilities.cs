namespace Obsidian.API._Types;

[Flags]
public enum PlayerAbility
{
    None = 0x00,
    Invulnerable = 0x01,
    Flying = 0x02,
    AllowFlying = 0x04,
    CreativeMode = 0x08
}
