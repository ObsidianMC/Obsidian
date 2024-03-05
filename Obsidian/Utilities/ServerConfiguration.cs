using Microsoft.Extensions.Logging;
using Obsidian.API.Config;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities;

public sealed class ServerConfiguration : IServerConfiguration
{
    public string Motd { get; set; } = $"§k||||§r §5Obsidian §cPre§r-§cRelease §r§k||||§r \n§r§lRunning on .NET §l§c{Environment.Version} §r§l<3";

    public int Port { get; set; } = 25565;

    public string JoinMessage { get; set; } = "§e{0} joined the game";

    public string LeaveMessage { get; set; } = "§e{0} left the game";

    public bool OnlineMode { get; set; } = true;
    public int MaxPlayers { get; set; } = 25;

    public bool AllowOperatorRequests { get; set; } = true;

    public bool AllowUntrustedPlugins { get; set; } = true;

    /// <summary>
    /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
    /// </summary>
    public bool MulitplayerDebugMode { get; set; } = false;

    public string Header { get; set; } = "§dObsidian-powered minecraft server";

    public string Footer { get; set; } = "§l( §cO §dw §cO §r§l)";

    public bool? Baah { get; set; }

    public bool WhitelistEnabled { get; set; }

    public bool IpWhitelistEnabled { get; set; }

    public List<string> WhitelistedIPs { get; set; } = new();
    public List<WhitelistedPlayer> Whitelisted { get; set; } = new();

    public long KeepAliveInterval { get; set; } = 10_000; // 10 seconds per KeepAlive

    public long KeepAliveTimeoutInterval { get; set; } = 30_000; // No response after 30s? Timeout

    public long ConnectionThrottle { get; set; } = 15_000;

    public string[] DownloadPlugins { get; set; } = [];
    public RconConfig? Rcon { get; set; }

    public bool AllowLan { get; set; } = true; // Enabled because it's super useful for debugging tbh

    public int PregenerateChunkRange { get; set; } = 15; // by default, pregenerate range from -15 to 15

    [JsonConverter(typeof(JsonStringEnumConverter))]
#if DEBUG
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
#else
        public LogLevel LogLevel { get; set; }  = LogLevel.Information;
#endif

    public bool DebugMode;

    public bool VerboseExceptionLogging { get; set; } = false;

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
