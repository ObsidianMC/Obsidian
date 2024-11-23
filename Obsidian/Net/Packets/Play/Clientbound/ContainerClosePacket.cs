
namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class ContainerClosePacket
{
    public required int ContainerId { get; init; }

    public override void Serialize(INetStreamWriter writer) => writer.WriteVarInt(this.ContainerId);
}
