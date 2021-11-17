namespace Obsidian.API;

[Flags]
public enum CommandIssuers
{
    None,
    Client,
    Console,
    RemoteConsole,
    Plugin,
    Any = ~None
}
