namespace Obsidian.Net.Packets;

public interface IServerboundPacket : IPacket
{
    public void Populate(INetStreamReader reader);
    public ValueTask HandleAsync(IServer server, IClientPlayer player);
    public ValueTask HandleAsync(IClient client);
}
