namespace Obsidian.API;

public enum Gamemode : sbyte
{
    None = -1,
    Survival,
    Creative,
    Adventure,
    Spectator,
    Hardcore = 0x8,
}
