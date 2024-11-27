using Obsidian.Entities;

namespace Obsidian.Net.Packets;

public abstract class ServerboundPacket : IServerboundPacket
{
    public abstract int Id { get; }

    public virtual void Populate(INetStreamReader reader) { }
    public virtual ValueTask HandleAsync(Server server, Player player) => default;
}
