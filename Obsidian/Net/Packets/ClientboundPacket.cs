namespace Obsidian.Net.Packets;

public abstract class ClientboundPacket : ISerializablePacket
{
    public abstract int Id { get; }

    public virtual void Serialize(MinecraftStream stream) { }
}
