using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Net.Packets;
using Obsidian.Utilities.Collections;

namespace Obsidian.Net.ClientHandlers;
internal abstract class ClientHandler
{
    protected Server Server => this.Client.server;
    protected ILogger Logger => this.Client.Logger;
    protected Player? Player => this.Client.Player;

    public required Client Client { get; init; }

    public abstract ValueTask<bool> HandleAsync(PacketData packetData);

    protected async ValueTask<bool> HandleFromPoolAsync<T>(byte[] data) where T : IServerboundPacket, new()
    {
        var packet = ObjectPool<T>.Shared.Rent();
        var success = true;
        try
        {
            using var mcStream = new MinecraftStream(data);
            packet.Populate(mcStream);
            await packet.HandleAsync(this.Server, this.Player);
        }
        catch (Exception e)
        {
            this.Logger.LogCritical(e, "An error has occured trying to populate a packet.");
            success = false;
        }
        ObjectPool<T>.Shared.Return(packet);

        return success;
    }

    protected void SendPacket(IClientboundPacket packet) => this.Client.SendPacket(packet);
}
