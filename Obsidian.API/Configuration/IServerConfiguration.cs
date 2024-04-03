namespace Obsidian.API.Configuration;

public interface IServerConfiguration
{
    public bool? Baah { get; set; }

    /// <summary>
    /// Allows the server to advertise itself as a LAN server to devices on your network.
    /// </summary>
    public bool AllowLan { get; set; }

    /// <summary>
    /// Server description.
    /// </summary>
    public string Motd { get; set; }

    /// <summary>
    /// The port on which to listen for incoming connection attempts.
    /// </summary>
    public int Port { get; set; }

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

    /// <summary>
    /// Enabled Remote Console operation.
    /// </summary>
    /// <remarks>See more at https://wiki.vg/RCON</remarks>
    public bool EnableRcon => Rcon is not null;

    public bool Whitelist { get; set; }

    /// <summary>
    /// Network Configuration
    /// </summary>
    public NetworkConfiguration Network { get; set; }

    /// <summary>
    /// Remote Console configuration
    /// </summary>
    public RconConfiguration? Rcon { get; set; }

    /// <summary>
    /// The view distance of the server.
    /// </summary>
    /// <remarks>
    /// Players with higher view distance will use the server's view distance.
    /// </remarks>
    public byte ViewDistance { get; set; }
}
