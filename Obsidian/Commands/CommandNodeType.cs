namespace Obsidian.Commands;

/// <summary>
/// https://wiki.vg/Command_Data#Flags
/// </summary>
[Flags]
public enum CommandNodeType : sbyte
{
    Root = 0x00,
    Literal = 0x01,
    Argument = 0x02,
    IsExecutable = 0x04,
    HasRedirect = 0x08,
    HasSuggestions = 0x10,
    IsRestricted = 0x20,
}
