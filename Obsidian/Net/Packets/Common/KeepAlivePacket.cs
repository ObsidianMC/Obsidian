using Microsoft.Extensions.Logging;
using Obsidian.Entities;
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

    public async override ValueTask HandleAsync(Client client)
    {
        var time = DateTimeOffset.Now;
        var player = client.Player!;
        var server = client.server;

        long keepAliveId = time.ToUnixTimeMilliseconds();
        if (keepAliveId - client.lastKeepAliveId > server.Configuration.Network.KeepAliveTimeoutInterval)
        {
            await client.DisconnectAsync("Timed out..");
            return;
        }

        client.Logger.LogDebug("Doing KeepAlive ({keepAliveId}) with {Username} ({Uuid})", keepAliveId, player.Username, player.Uuid);

        this.KeepAliveId = keepAliveId;

        client.SendPacket(this);

        client.lastKeepAliveId = keepAliveId;
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var client = player.client;

        if (this.KeepAliveId != client.lastKeepAliveId)
        {
            client.Logger.LogWarning("Received invalid KeepAlive from {Username}?? Naughty???? ({Uuid})", player.Username, player.Uuid);
            await client.DisconnectAsync("Kicked for invalid KeepAlive.");
            return;
        }

        // from now on we know this keepalive is VALID and WITHIN BOUNDS
        decimal ping = DateTimeOffset.Now.ToUnixTimeMilliseconds() - this.KeepAliveId;
        ping = Math.Min(int.MaxValue, ping); // convert within integer bounds
        ping = Math.Max(0, ping); // negative ping is impossible.

        client.Ping = (int)ping;
        client.Logger.LogDebug("Valid KeepAlive ({KeepAliveId}) handled from {Username} ({Uuid})", this.KeepAliveId, player.Username, player.Uuid);
        // KeepAlive is handled.

        client.lastKeepAliveId = null;
    }
}
