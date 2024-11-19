namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class BundledPacket : ClientboundPacket
{
    public required List<ClientboundPacket> Packets { get; set; }

    public override int Id => 0;

    public override void Serialize(MinecraftStream stream)
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
