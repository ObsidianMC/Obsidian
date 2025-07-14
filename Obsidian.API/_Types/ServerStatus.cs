using Obsidian.API.Configuration;
using Obsidian.API.Utilities;
using System.IO;

namespace Obsidian.API;
public sealed class ServerStatus
{
    private static ReadOnlySpan<byte> PngHeader => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public ServerVersion Version { get; private set; }

    public ServerPlayers? Players { get; private set; }

    public ServerDescription Description { get; private set; }

    /// <summary>
    /// This is a base64 png image, that has dimensions of 64x64.
    /// </summary>
    public string Favicon { get; set; }

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

public readonly struct ServerVersion
{
    public required string Name { get; init; }
    public required ProtocolVersion Protocol { get; init; }
}

public sealed class ServerPlayers
{
    public int Max { get; set; }
    public int Online => this.Sample.Count;

    public List<object> Sample { get; set; } = [];

    public ServerPlayers(IServer server)
    {
        Max = server.Configuration.MaxPlayers;

        foreach (var player in server.OnlinePlayers.Values)
        {
            if (!player.ClientInformation.AllowServerListings)
                continue;

            this.AddPlayer(player.Username, player.Uuid);
        }
    }

    public void Clear() => Sample.Clear();

    public void AddPlayer(string username, Guid uuid) => Sample.Add(new
    {
        name = username,
        id = uuid
    });
}

public readonly struct ServerDescription(ServerConfiguration config)
{
    public string Text => FormatText(config.Motd);

    private static string FormatText(string text) => text.Replace('&', '§');
}
