using Obsidian.Entities;

namespace Obsidian.Net.Packets;

public interface IServerboundPacket : IPacket
{
    public void Populate(INetStreamReader reader);
    public ValueTask HandleAsync(Server server, Player player);
}
