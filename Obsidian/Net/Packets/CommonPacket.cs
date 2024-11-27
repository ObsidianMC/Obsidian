using Obsidian.Entities;

namespace Obsidian.Net.Packets;
public abstract record class CommonPacket : IClientboundPacket, IServerboundPacket
{
    public virtual int Id { get; init; }

    public virtual void Serialize(INetStreamWriter writer) { }

    public virtual void Populate(INetStreamReader reader) { }
    public virtual ValueTask HandleAsync(Server server, Player player) => default;
}
