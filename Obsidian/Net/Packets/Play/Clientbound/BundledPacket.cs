namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Represents a bundle of packets that the client should handle in the same tick.
/// A <see cref="BundleDelimiterPacket"/> is sent before and after the content packets are sent to the client.
/// Vanilla Minecraft client doesn't allow more than 4096 packets in one bundle.
/// </summary>
public partial class BundledPacket(List<ClientboundPacket> packets) : ClientboundPacket
{
    /// <summary>
    /// The packets in this bundle.
    /// </summary>
    public List<ClientboundPacket> Packets { get; set; } = packets;

    public override int Id => 0;

    public override void Serialize(INetStreamWriter writer)
    {
        foreach (var packet in this.Packets)
            writer.WritePacket(packet);
    }
}
