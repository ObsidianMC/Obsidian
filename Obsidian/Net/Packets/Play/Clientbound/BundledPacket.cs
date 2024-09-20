namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed class BundledPacket : IClientboundPacket
{
    public required List<IClientboundPacket> Packets { get; set; }

    public int Id => 0x00;

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        foreach (var packet in this.Packets)
            packet.Serialize(packetStream);

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength());
        stream.WriteVarInt(Id);

        packetStream.Position = 0;
        packetStream.CopyTo(stream);

        stream.WriteVarInt(Id.GetVarIntLength());
        stream.WriteVarInt(Id);
        stream.Lock.Release();
    }
}
