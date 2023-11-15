namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class ChunkBatchStartPacket : IClientboundPacket
{
    public int Id => 0x0D;

    //TODO FIX SOURCE GENS WITH PACKETS THAT HAVE NO FIELDS
    public void Serialize(MinecraftStream stream) { }
}
