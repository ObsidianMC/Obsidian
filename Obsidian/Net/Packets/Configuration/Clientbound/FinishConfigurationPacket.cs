namespace Obsidian.Net.Packets.Configuration.Clientbound;
public sealed class FinishConfigurationPacket : IClientboundPacket
{
    public int Id => 0x02;

    public void Serialize(MinecraftStream stream)
    {

    }
}
