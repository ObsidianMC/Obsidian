using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetHeldSlotPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public override void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteByte((sbyte)Slot);

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
