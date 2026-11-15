namespace Obsidian.API;

/// <summary>Where a command was sent from. Commands combine these flags to say who may run them.</summary>
[Flags]
public enum CommandIssuers
{
    /// <summary>No issuer.</summary>
    None,
    /// <summary>A player in game.</summary>
    Client,
    /// <summary>The server console.</summary>
    Console,
    /// <summary>A remote console (RCON) connection.</summary>
    /// <remarks>Its value, 3, has the same bits as <c>Client | Console</c>.</remarks>
    RemoteConsole,
    /// <summary>A plugin.</summary>
    Plugin,
    /// <summary>Every issuer.</summary>
    Any = ~None
}
