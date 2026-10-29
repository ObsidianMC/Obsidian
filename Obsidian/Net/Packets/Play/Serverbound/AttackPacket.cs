using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// A player's attack on an entity, split from <see cref="InteractPacket"/> in 26.1. The arm swing that goes with it
/// comes in <see cref="PunchPacket"/>.
/// </summary>
public partial class AttackPacket
{
    public int EntityId { get; private set; }

    public override void Populate(INetStreamReader reader) => this.EntityId = reader.ReadVarInt();

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        // Since 26.1.2 a spectator's attack is dropped before anything else, as vanilla's handleAttack does.
        if (player.GameMode == GameMode.Spectator)
            return;

        if (InteractPacket.FindTarget(player, this.EntityId) is not { } entity)
            return;

        // A spear's jab is a stab action, not an attack.
        if (CombatItems.Component(player.GetHeldItem(), "piercing_weapon").ValueKind == System.Text.Json.JsonValueKind.Object)
            return;

        // Like vanilla, a client that attacks an item, an experience orb, an arrow or itself is disconnected.
        if (entity is ItemEntity or ExperienceOrb or Arrow || ReferenceEquals(entity, player))
        {
            await player.KickAsync(new ChatMessage { Translate = "multiplayer.disconnect.invalid_entity_attacked" });
            return;
        }

        await player.AttackAsync(entity, player.Sneaking);
    }
}
