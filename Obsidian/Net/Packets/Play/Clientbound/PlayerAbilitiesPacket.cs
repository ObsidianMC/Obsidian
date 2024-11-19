using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerAbilitiesPacket
{
    public PlayerAbility Abilities { get; set; } = PlayerAbility.None;

    public float FlyingSpeed { get; set; } = 0.05F;

    public float FieldOfViewModifier { get; set; } = 0.1F;

    public override void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();
        packetStream.WriteByte((byte)Abilities);
        packetStream.WriteFloat(FlyingSpeed);
        packetStream.WriteFloat(FieldOfViewModifier);

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
