using Obsidian.Entities;

namespace Obsidian.Net.Packets;
public abstract record class CommonPacket : IClientboundPacket, IServerboundPacket
{
    public virtual int Id { get; init; }

    public virtual void Serialize(MinecraftStream stream) { }

    public virtual void Populate(byte[] data) { }
    public virtual void Populate(MinecraftStream stream) { }
    public virtual ValueTask HandleAsync(Server server, Player player) => default;
}
