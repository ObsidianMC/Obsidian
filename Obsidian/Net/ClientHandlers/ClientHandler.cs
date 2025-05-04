using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Net.Packets;
using Obsidian.Utilities.Collections;

namespace Obsidian.Net.ClientHandlers;
internal abstract class ClientHandler
{
    protected Server Server => (Server)this.Client.Server;
    protected ILogger Logger => this.Client.Logger;
    protected Player? Player => (Player)this.Client.Player;

    public required Client Client { get; init; }

    public abstract ValueTask<bool> HandleAsync(PacketData packetData);

    protected async ValueTask<bool> HandleFromPoolAsync<T>(NetworkBuffer data) where T : class, IServerboundPacket, new()
    {
        var packet = SimpleObjectPool<T>.Shared.Get();

        var success = true;
        try
        {
            packet.Populate(data);
            await packet.HandleAsync(this.Server, this.Player);
        }
        catch (Exception e)
        {
            this.Logger.LogCritical(e, "An error has occured trying to populate a packet.");
            success = false;
        }
        SimpleObjectPool<T>.Shared.Return(packet);

        return success;
    }

    protected void SendPacket(IClientboundPacket packet) => this.Client.SendPacket(packet);
}
