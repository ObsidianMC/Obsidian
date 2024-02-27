using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Clientbound;

[Flags]
public enum PlayerAbility
{
    None = 0x00,
    Invulnerable = 0x01,
    Flying = 0x02,
    AllowFlying = 0x04,
    CreativeMode = 0x08
}

public class PlayerAbilitiesPacket : IClientboundPacket, IServerboundPacket
{
    public PlayerAbility Abilities { get; set; } = PlayerAbility.None;

    public float FlyingSpeed { get; set; } = 0.05F;

    public float FieldOfViewModifier { get; set; } = 0.1F;

    public int Id { get; }

    public PlayerAbilitiesPacket(bool toClient)
    {
        Id = toClient ? 0x36 : 0x20;
    }

    public void Serialize(MinecraftStream stream)
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

    public void Populate(MinecraftStream stream)
    {
        Abilities = (PlayerAbility) stream.ReadByte();
    }

    public void Populate(byte[] data)
    {
        using var stream = new MinecraftStream(data);
        Populate(stream);
    }

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (Abilities.HasFlag(PlayerAbility.Flying)
            && !Abilities.HasFlag(PlayerAbility.AllowFlying)
            && player.Gamemode is not Gamemode.Creative or Gamemode.Spectator)
        {
            await player.KickAsync("Cheating is not allowed!");
        }

        player.Abilities |= Abilities;
    }
}
