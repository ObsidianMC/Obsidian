using Obsidian.Entities;

namespace Obsidian.Net.Packets;

public interface IServerboundPacket : IPacket
{
    public void Populate(byte[] data);
    public void Populate(MinecraftStream stream);

    /// <summary>
    /// Called when client state is in or after Configuration.
    /// </summary>
    public ValueTask HandleAsync(Server server, Player player);

    /// <summary>
    /// Called when client state is pre Configuration.
    /// </summary>
    public ValueTask HandleAsync(Client client);
}
