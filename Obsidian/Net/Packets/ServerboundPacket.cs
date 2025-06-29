namespace Obsidian.Net.Packets;

public abstract class ServerboundPacket : IServerboundPacket
{
    public abstract int Id { get; }

    public virtual void Populate(INetStreamReader reader) { }
    public virtual ValueTask HandleAsync(IServer server, IPlayer player) => default;
    public virtual ValueTask HandleAsync(IClient client) => default;
}
