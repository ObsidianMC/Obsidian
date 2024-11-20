using Obsidian.Entities;

namespace Obsidian.Net.Packets;

public interface IPacket
{
    public int Id { get; }
}

public interface IClientboundPacket : IPacket
{
    public void Serialize(INetStreamWriter writer);
}

public interface IServerboundPacket : IPacket
{
    public void Populate(INetStreamReader reader);
    public ValueTask HandleAsync(Server server, Player player);
}
