using Obsidian.Entities;

namespace Obsidian.Net.Packets;

public interface IPacket
{
    public int Id { get; }
}

public interface IClientboundPacket : IPacket
{
    public void Serialize(MinecraftStream stream);
}

public interface IServerboundPacket : IPacket
{
    public void Populate(byte[] data);
    public void Populate(MinecraftStream stream);
    public ValueTask HandleAsync(Server server, Player player);
}
