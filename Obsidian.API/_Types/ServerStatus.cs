using Obsidian.API.Configuration;
using Obsidian.API.Utilities;
using System.IO;
using System.Runtime.CompilerServices;

namespace Obsidian.API;
/// <summary>
/// The status a server sends to the multiplayer server list: version, player counts, message of the day and icon.
/// </summary>
public sealed class ServerStatus
{
    private static ReadOnlySpan<byte> PngHeader => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Gets the server's version name and protocol number.</summary>
    public ServerVersion Version { get; private set; }

    /// <summary>Gets the player counts and sample, or null when the status is anonymous.</summary>
    public ServerPlayers? Players { get; private set; }

    /// <summary>Gets the message of the day.</summary>
    public ServerDescription Description { get; private set; }

    /// <summary>
    /// This is a base64 png image, that has dimensions of 64x64.
    /// </summary>
    public string Favicon { get; set; }

    /// <summary>
    /// Whether plugins registered payload channels, so connecting may need client mods. NeoForge clients read this
    /// field of the status response to mark the server as modded.
    /// </summary>
    public bool IsModded { get; set; }

    /// <summary>
    /// Generates a server status from the specified <paramref name="server"/>.
    /// </summary>
    public ServerStatus(IServer server, string serverIcon, bool anonymous = false)
    {
        ArgumentNullException.ThrowIfNull(server);

        Version = new ServerVersion
        {
            Name = $"Obsidian {server.Protocol.GetDescription()}",
            Protocol = server.Protocol
        };

        if (!anonymous)
            Players = new ServerPlayers(server);

        Description = new ServerDescription(server.Configuration);

        var faviconFile = "favicon.png";
        if (File.Exists(faviconFile))
        {
            byte[] imageData = File.ReadAllBytes(@"favicon.png");
            bool isValidImage = imageData.Length >= PngHeader.Length && PngHeader.SequenceEqual(imageData.AsSpan(0, PngHeader.Length));
            if (isValidImage)
            {
                string b64 = Convert.ToBase64String(imageData);
                Favicon = $"data:image/png;base64,{b64}";
            }
            else
            {
                Favicon = serverIcon;
            }
        }
        else
        {
            Favicon = serverIcon;
        }
    }
}

/// <summary>The version a server reports in its status.</summary>
public readonly record struct ServerVersion
{
    /// <summary>Gets the version name shown in the server list.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the protocol version; clients with a different one show the server as incompatible.</summary>
    public required ProtocolVersion Protocol { get; init; }
}

/// <summary>The player counts and sample a server reports in its status.</summary>
public sealed class ServerPlayers
{
    /// <summary>Gets or sets the maximum player count shown.</summary>
    public int Max { get; set; }
    /// <summary>Gets the online player count shown, which is the number of players in <see cref="Sample"/>.</summary>
    public int Online => this.Sample.Count;

    /// <summary>
    /// Gets or sets the players listed when hovering over the player count; add them with <see cref="AddPlayer"/>.
    /// </summary>
    public List<object> Sample { get; set; } = [];

    /// <summary>Creates the counts for a server, listing every online player who allows being listed.</summary>
    public ServerPlayers(IServer server)
    {
        Max = server.Configuration.MaxPlayers;

        foreach (var player in server.OnlinePlayers.Values)
        {
            if (!player.ClientInformation.AllowsListing)
                continue;

            this.AddPlayer(player.Username, player.Uuid);
        }
    }

    /// <summary>Removes every player from <see cref="Sample"/>.</summary>
    public void Clear() => Sample.Clear();

    /// <summary>Adds a player to <see cref="Sample"/>.</summary>
    public void AddPlayer(string username, Guid uuid) => Sample.Add(new
    {
        name = username,
        id = uuid
    });
}

/// <summary>The message of the day a server reports in its status, read from its configuration each time.</summary>
/// <param name="config">The configuration whose <see cref="ServerConfiguration.Motd"/> is shown.</param>
public readonly struct ServerDescription(ServerConfiguration config) : IEquatable<ServerDescription>
{
    private readonly ServerConfiguration config = config;

    /// <summary>Gets the message of the day, with <c>&amp;</c> formatting codes turned into <c>§</c> codes.</summary>
    public string Text => FormatText(this.config.Motd);

    /// <summary>Indicates whether two descriptions read the same configuration object.</summary>
    public static bool operator ==(ServerDescription left, ServerDescription right) => left.Equals(right);

    /// <summary>Indicates whether two descriptions read different configuration objects.</summary>
    public static bool operator !=(ServerDescription left, ServerDescription right) => !left.Equals(right);

    // Text is derived from the configuration, so describing the same configuration instance means equal.
    /// <summary>
    /// Indicates whether this description reads the same configuration object as <paramref name="other"/>.
    /// </summary>
    public bool Equals(ServerDescription other) => ReferenceEquals(this.config, other.config);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ServerDescription description && this.Equals(description);

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this.config);

    private static string FormatText(string text) => text.Replace('&', '§');
}
