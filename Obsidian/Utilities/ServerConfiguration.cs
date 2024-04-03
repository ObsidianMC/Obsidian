using Obsidian.API.Configuration;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities;

public sealed class ServerConfiguration : IServerConfiguration
{
    private byte viewDistance = 10;

    // Anything lower than 3 will cause weird artifacts on the client.
    private const byte MinimumViewDistance = 3;

    public string Motd { get; set; } = $"§k||||§r §5Obsidian §cPre§r-§cRelease §r§k||||§r \n§r§lRunning on .NET §l§c{Environment.Version} §r§l<3";

    public int Port { get; set; } = 25565;

    public bool OnlineMode { get; set; } = true;
    public int MaxPlayers { get; set; } = 25;

    public bool AllowOperatorRequests { get; set; } = true;

    public bool? Baah { get; set; }

    public bool Whitelist { get; set; }

    public NetworkConfiguration Network { get; set; } = new();

    public RconConfiguration? Rcon { get; set; }

    public bool AllowLan { get; set; } = true; // Enabled because it's super useful for debugging tbh

    public byte ViewDistance
    {
        get => viewDistance;
        set => viewDistance = value >= MinimumViewDistance ? value : MinimumViewDistance;
    }

    public int PregenerateChunkRange { get; set; } = 15; // by default, pregenerate range from -15 to 15;

    public ServerListQuery ServerListQuery { get; set; } = ServerListQuery.Full;

    public int TimeTickSpeedMultiplier { get; set; } = 1;
}

public sealed class ServerWorld
{
    public string Name { get; set; } = "overworld";
    public string Generator { get; set; } = "overworld";

    public string Seed { get; set; } = Globals.Random.Next().ToString();

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
