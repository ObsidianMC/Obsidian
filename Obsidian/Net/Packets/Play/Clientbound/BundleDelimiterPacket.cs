namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class BundleDelimiterPacket : IClientboundPacket
{
    public int Id => 0x00;

    public void Serialize(MinecraftStream stream) { }
}
