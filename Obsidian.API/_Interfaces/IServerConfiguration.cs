using Obsidian.API.Config;

namespace Obsidian.API;

public interface IServerConfiguration
{
    public bool? Baah { get; set; }

    /// <summary>
    /// Returns true if <see cref="ConnectionThrottle"/> has a value greater than 0.
    /// </summary>
    public bool CanThrottle => this.ConnectionThrottle > 0;

    /// <summary>
    /// Allows the server to advertise itself as a LAN server to devices on your network.
    /// </summary>
    public bool AllowLan { get; set; }

    public bool IpWhitelistEnabled { get; set; }

    /// <summary>
    /// The time in milliseconds to wait before an ip is allowed to try and connect again.
    /// </summary>
    public long ConnectionThrottle { get; set; }

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
    public int PregenerateChunkRange { get; set; }

    /// <summary>
    /// The speed at which world time & rain time go by.
    /// </summary>
    public int TimeTickSpeedMultiplier { get; set; }

    /// <summary>
    /// Allow people to requests to become an operator.
    /// </summary>
    public bool AllowOperatorRequests { get; set; }

    public bool WhitelistEnabled { get; set; }

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
    /// Interval between KeepAlive packets send by the server.
    /// </summary>
    public long KeepAliveInterval { get; set; }

    /// <summary>
    /// How long it should take for the server to kick an inactive client. KeepAlive Timeout.
    /// </summary>
    public long KeepAliveTimeoutInterval { get; set; }

    /// <summary>
    /// Paths of plugins that are loaded at the starttime.
    /// </summary>
    public string[] DownloadPlugins { get; set; }

    /// <summary>
    /// Enabled Remote Console operation.
    /// </summary>
    /// <remarks>See more at https://wiki.vg/RCON</remarks>
    public bool EnableRcon => Rcon is not null;

    public bool VerboseExceptionLogging { get; set; }

    /// <summary>
    /// Remote Console configuration
    /// </summary>
    public RconConfig? Rcon { get; set; }

    /// <summary>
    /// The view distance of the server.
    /// </summary>
    /// <remarks>
    /// Players with higher view distance will use the server's view distance.
    /// </remarks>
    public byte ViewDistance { get; set; }

    public List<string> WhitelistedIPs { get; set; }
    public List<WhitelistedPlayer> Whitelisted { get; set; }
}
