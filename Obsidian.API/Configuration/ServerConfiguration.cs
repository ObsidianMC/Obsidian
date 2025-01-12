using System.Text.Json.Serialization;

namespace Obsidian.API.Configuration;

public sealed class ServerConfiguration
{
    private const byte DefaultViewDistance = 10;
    private const ushort DefaultEntityBroadcastRangePercentage = 100;

    // Anything lower than 3 will cause weird artifacts on the client.
    private const byte MinimumViewDistance = 3;
    private const byte MinimumSimulationDistance = 5;
    /// <summary>
    /// Enabled Remote Console operation.
    /// </summary>
    /// <remarks>See more at https://wiki.vg/RCON</remarks>
    public bool EnableRcon => Rcon is not null;

    /// <summary>
    /// Server description.
    /// </summary>
    public string Motd { get; set; } = $"§k||||§r §5Obsidian §cPre§r-§cRelease §r§k||||§r \n§r§lRunning on .NET §l§c{Environment.Version} §r§l<3";

    /// <summary>
    /// The port on which to listen for incoming connection attempts.
    /// </summary>
    public int Port { get; set; } = 25565;

    /// <summary>
    /// Whether the server uses MojangAPI for loading skins etc.
    /// </summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>
    /// Maximum amount of players that is allowed to be connected at the same time.
    /// </summary>
    public int MaxPlayers { get; set; } = 25;

    /// <summary>
    /// Allow people to requests to become an operator.
    /// </summary>
    public bool AllowOperatorRequests { get; set; } = true;

    public bool ServerShutdownStopsProgram { get; set; } = true;

    /// <summary>
    /// Whether to allow the server to load untrusted(unsigned) plugins
    /// </summary>
    public bool AllowUntrustedPlugins { get; set; } = true;

    public bool? Baah { get; set; }

    public bool Whitelist { get; set; }

    /// <summary>
    /// Network Configuration
    /// </summary>
    public NetworkConfiguration Network { get; set; } = new();

    /// <summary>
    /// Remote Console configuration
    /// </summary>
    public RconConfiguration? Rcon { get; set; }

    /// <summary>
    /// Messages that the server will use by default for various actions.
    /// </summary>
    public MessagesConfiguration Messages { get; set; } = new();

    /// <summary>
    /// Allows the server to advertise itself as a LAN server to devices on your network.
    /// </summary>
    public bool AllowLan { get; set; } = true; // Enabled because it's super useful for debugging tbh

    /// <summary>
    /// The view distance of the server.
    /// </summary>
    /// <remarks>
    /// Players with higher view distance will use the server's view distance.
    /// </remarks>   

    public byte ViewDistance
    {
        get => field == 0 ? DefaultViewDistance : field;
        set => field = value >= MinimumViewDistance ? value : MinimumViewDistance;
    }

    public byte SimulationDistance
    {
        get => field == 0 ? DefaultViewDistance : field;
        set => field = value > this.ViewDistance ? value >= MinimumSimulationDistance ? value : MinimumSimulationDistance : ViewDistance;
    }

    public ushort EntityBroadcastRangePercentage
    {
        get => field == 0 ? DefaultEntityBroadcastRangePercentage : field;
        set => field = Math.Max((ushort)10, value);
    }

    public int PregenerateChunkRange { get; set; } = 15; // by default, pregenerate range from -15 to 15;

    public ServerListQuery ServerListQuery { get; set; } = ServerListQuery.Full;

    /// <summary>
    /// The speed at which world time & rain time go by.
    /// </summary>
    public int TimeTickSpeedMultiplier { get; set; } = 1;
}

public sealed class ServerWorld
{
    public string Name { get; set; } = "overworld";
    public string Generator { get; set; } = "overworld";

    public string Seed { get; set; } = default!;

    public bool Default { get; set; }

    public string DefaultDimension { get; set; } = "minecraft:overworld";

    public List<string> ChildDimensions { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerListQuery
{
    Full,
    Anonymized,
    Disabled
}
