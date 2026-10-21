using Obsidian.API.Inventory;
namespace Obsidian.Entities;

/// <summary>
/// A rideable minecart, like vanilla's <c>Minecart</c>: a player who isn't sneaking gets in, a moving empty cart
/// scoops up the mobs it runs into, and a powered activator rail throws its passengers out.
/// </summary>
[MinecraftEntity("minecraft:minecart")]
public sealed partial class Minecart : AbstractMinecart
{
    public Minecart() => this.Type = EntityType.Minecart;

    internal override bool IsRideable => true;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.Minecart);

    public override ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) =>
        ValueTask.FromResult(!player.Sneaking && this.Passengers.Count == 0 && this.Board(player));

    // Vanilla's Minecart.activateMinecart: a powered activator rail ejects the passengers and shakes the cart.
    private protected override ValueTask ActivateAsync(Vector rail, bool powered)
    {
        if (!powered)
            return default;

        if (this.Passengers.Count > 0)
            this.EjectPassengers();

        if (this.HurtTime == 0)
        {
            this.HurtDirection = -this.HurtDirection;
            this.HurtTime = 10;
            this.Damage = 50;
        }

        return default;
    }
}
