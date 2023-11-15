namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO WRITE
public sealed partial class StartConfigurationPacket : IClientboundPacket
{
    public int Id => 0x65;

    public void Serialize(MinecraftStream stream) { }
}
