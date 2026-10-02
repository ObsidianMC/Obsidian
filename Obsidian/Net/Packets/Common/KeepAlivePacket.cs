using Microsoft.Extensions.Logging;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class KeepAlivePacket
{
    [Field(0)]
    public long KeepAliveId { get; set; }

    public KeepAlivePacket() { }

    public KeepAlivePacket(long id)
    {
        KeepAliveId = id;
    }

    public override void Populate(INetStreamReader stream)
    {
        this.KeepAliveId = stream.ReadLong();
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteLong(KeepAliveId);
    }

    public async override ValueTask HandleAsync(IClient client)
    {
        var time = DateTimeOffset.Now;
        var player = client.Player!;
        var server = client.Server;

        long keepAliveId = time.ToUnixTimeMilliseconds();
        if (keepAliveId - client.LastKeepAliveId > server.Configuration.Network.KeepAliveTimeoutInterval)
        {
            await client.DisconnectAsync("Timed out..");
            return;
        }

        this.KeepAliveId = keepAliveId;

        client.SendPacket(this);

        client.LastKeepAliveId = keepAliveId;
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var client = player.Client;

        if (this.KeepAliveId != client.LastKeepAliveId)
        {
            Log.InvalidKeepAlive(client.Logger, player.Username);
            await client.DisconnectAsync("Kicked for invalid KeepAlive.");
            return;
        }

        // from now on we know this keepalive is VALID and WITHIN BOUNDS
        decimal ping = DateTimeOffset.Now.ToUnixTimeMilliseconds() - this.KeepAliveId;
        ping = Math.Min(int.MaxValue, ping); // convert within integer bounds
        ping = Math.Max(0, ping); // negative ping is impossible.

        client.Ping = (int)ping;
        // KeepAlive is handled.

        client.LastKeepAliveId = null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting {Username}: invalid keep-alive")]
        public static partial void InvalidKeepAlive(ILogger logger, string username);
    }
}
