namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerAbilitiesPacket
{
    public PlayerAbility Abilities { get; set; } = PlayerAbility.None;

    public float FlyingSpeed { get; set; } = 0.05F;

    public float WalkingSpeed { get; set; } = 0.1F;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteByte(Abilities);
        writer.WriteFloat(FlyingSpeed);
        writer.WriteFloat(WalkingSpeed);
    }
}
