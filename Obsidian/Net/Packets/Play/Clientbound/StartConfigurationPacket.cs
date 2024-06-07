namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO WRITE
public sealed partial class StartConfigurationPacket : IClientboundPacket
{
    public int Id => 0x69;

    public void Serialize(MinecraftStream stream) => this.WritePacketId(stream);
}
