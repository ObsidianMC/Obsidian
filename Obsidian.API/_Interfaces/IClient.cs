namespace Obsidian.API;
public interface IClient : IDisposable
{
    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public IPlayer? Player { get; }

    public bool SendPacket(IClientboundPacket packet);

    public ValueTask QueuePacketAsync(IClientboundPacket packet);
}
