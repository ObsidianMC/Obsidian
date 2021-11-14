using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets;

public interface IServerboundPacket : IPacket
{
    public void Populate(byte[] data);
    public void Populate(MinecraftStream stream);
    public ValueTask HandleAsync(Server server, Player player);
}
