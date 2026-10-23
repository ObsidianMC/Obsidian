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

    // Like vanilla's handlePlayerAbilities: the client only says whether it flies, which counts while it may fly (in
    // creative or spectator, or with flight granted, see MovementAbilities.CanFly).
    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var flying = this.Abilities.HasFlag(PlayerAbility.Flying)
            && player.Abilities.HasFlag(PlayerAbility.AllowFlying);

        player.Abilities = flying
            ? player.Abilities | PlayerAbility.Flying
            : player.Abilities & ~PlayerAbility.Flying;

        return default;
    }
}
