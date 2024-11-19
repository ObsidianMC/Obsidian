namespace Obsidian.Net.Packets;

public abstract class ClientboundPacket : IPacket
{
    public abstract int Id { get; }

    public virtual void Serialize(MinecraftStream stream) { }
}
