using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TakeItemEntityPacket
{
    [Field(0), VarLength]
    public required int CollectedEntityId { get; init; }

    [Field(1), VarLength]
    public required int CollectorEntityId { get; init; }

    [Field(2), VarLength]
    public required int PickupItemCount { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.CollectedEntityId);
        writer.WriteVarInt(this.CollectorEntityId);
        writer.WriteVarInt(this.PickupItemCount);
    }
}
