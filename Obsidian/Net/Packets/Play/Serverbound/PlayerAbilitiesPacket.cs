namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class PlayerAbilitiesPacket
{
    public PlayerAbility Abilities { get; set; } = PlayerAbility.None;

    public float FlyingSpeed { get; set; } = 0.05F;

    public float FieldOfViewModifier { get; set; } = 0.1F;

    public override void Populate(INetStreamReader reader)
    {
        Abilities = reader.ReadUnsignedByte<PlayerAbility>();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (Abilities.HasFlag(PlayerAbility.Flying)
            && !Abilities.HasFlag(PlayerAbility.AllowFlying)
            && player.GameMode is not (GameMode.Creative or GameMode.Spectator))
        {
            await player.KickAsync("Cheating is not allowed!");
        }

        player.Abilities |= Abilities;
    }
}
