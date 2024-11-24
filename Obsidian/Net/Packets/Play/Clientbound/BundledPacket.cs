namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class BundledPacket : ClientboundPacket
{
    public required List<ClientboundPacket> Packets { get; set; }

    public override int Id => 0;

    public override void Serialize(INetStreamWriter writer)
    {
        foreach (var packet in this.Packets)
            writer.WritePacket(packet);
    }
}
