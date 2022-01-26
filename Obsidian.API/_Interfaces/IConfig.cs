﻿namespace Obsidian.API;

public interface IConfig
{
    /// <summary>
    /// Server description.
    /// </summary>
    public string Motd { get; set; }

    /// <summary>
    /// The port on which to listen for incoming connection attempts.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Message, that is sent to the chat when player successfully joins the server.
    /// </summary>
    public string JoinMessage { get; set; }

    /// <summary>
    /// Message, that is sent to the chat when player leaves the server.
    /// </summary>
    public string LeaveMessage { get; set; }

    /// <summary>
    /// Whether the server uses MojangAPI for loading skins etc.
    /// </summary>
    public bool OnlineMode { get; set; }

    /// <summary>
    /// Maximum amount of players that is allowed to be connected at the same time.
    /// </summary>
    public int MaxPlayers { get; set; }

    public bool AllowOperatorRequests { get; set; }

    /// <summary>
    /// Whether each login/client gets a random username where multiple connections from the same host will be allowed.
    /// </summary>
    public bool MulitplayerDebugMode { get; set; }

    /// <summary>
    /// Upper text in the in-game TAB menu.
    /// </summary>
    public string Header { get; set; }

    /// <summary>
    /// Lower text in the in-game TAB menu.
    /// </summary>
    public string Footer { get; set; }

    /// <summary>
    /// How many KeepAlive packets can be ignored by the client before disconnecting.
    /// </summary>
    public int MaxMissedKeepAlives { get; set; }

    /// <summary>
    /// Paths of plugins that are loaded at the starttime.
    /// </summary>
    public string[] DownloadPlugins { get; set; }
}
