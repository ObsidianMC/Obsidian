namespace Obsidian.API;

public enum Gamemode : byte
{
    Survival,
    Creative,
    Adventure,
    Spectator,
    Hardcore = 0x8,
}
