using Microsoft.Extensions.Logging;

namespace Obsidian.API;
public interface IClient : IDisposable
{
    public int Id { get; }
    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; }
    public string? Ip { get; }

    public int Ping { get; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public IPlayer? Player { get; }

    public ILogger Logger { get; }

    public bool SendPacket(IClientboundPacket packet);

    public ValueTask DisconnectAsync(ChatMessage reason);
    public ValueTask QueuePacketAsync(IClientboundPacket packet);
}
