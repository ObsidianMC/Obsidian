using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetPassengersPacket
{
    [Field(0), VarLength]
    public required int EntityId { get; init; }
    [Field(1)]
    public required int[] Passengers { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(EntityId);
        writer.WriteVarInt(Passengers.Length);
        foreach (var passenger in Passengers)
            writer.WriteVarInt(passenger);
    }
}
