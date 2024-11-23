namespace Obsidian.API;
public interface IPacket
{
    public int Id { get; }
}

public interface IClientboundPacket : IPacket
{
    public void Serialize(INetStreamWriter writer);
}
