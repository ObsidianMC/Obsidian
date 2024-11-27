using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class PlayerInfoRemovePacket
{
    [Field(0)]
    public required List<Guid> UUIDs { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.UUIDs.Count);

        foreach (var uuid in this.UUIDs)
            writer.WriteUuid(uuid);
    }
}
