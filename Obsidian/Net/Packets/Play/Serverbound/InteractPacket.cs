using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// A player's use of an entity (right-clicking it). Since 26.1 attacks come in <see cref="AttackPacket"/>, and every
/// use carries where on the entity the player clicked.
/// </summary>
public partial class InteractPacket
{
    public int EntityId { get; private set; }

    public InteractionHand Hand { get; private set; }

    /// <summary>
    /// Where on the entity the player clicked, relative to the entity's position, in low precision.
    /// </summary>
    public VectorF Location { get; private set; }

    public bool Sneaking { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.Hand = reader.ReadVarInt<InteractionHand>();
        var location = reader.ReadVelocity();
        this.Location = new VectorF((float)location.X, (float)location.Y, (float)location.Z);
        this.Sneaking = reader.ReadBoolean();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (FindTarget(player, this.EntityId) is not { } entity || player is not PlayerBase user)
            return;

        await user.InteractAsync(entity, this.Hand, this.Location, this.Sneaking);
    }

    /// <summary>
    /// The entity, or ender dragon part, a client's attack or use names, if it is near enough to reach.
    /// </summary>
    internal static IEntity? FindTarget(IClientPlayer player, int entityId) =>
        player.GetEntitiesNear(player.GameMode == GameMode.Creative ? 8 : 6).FirstOrDefault(x => x.EntityId == entityId)
        ?? player.GetEntitiesNear(24).OfType<EnderDragon>()
            .SelectMany(dragon => dragon.Parts)
            .FirstOrDefault(part => part.EntityId == entityId && part.IsInRange(player, 4));
}
