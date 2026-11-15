using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>
/// A plain container: a chest's or shulker box's slots, a dispenser's, an ender chest's, or a player's inventory.
/// </summary>
public sealed class Container : BaseContainer
{
    private const int PlayerHotbarStart = 36;
    private const int PlayerHotbarEnd = PlayerHotbarStart + 8;
    private const int PlayerMainInventoryStart = 9;
    private const int PlayerMainInventoryEnd = PlayerHotbarStart - 1;

    internal int StateId { get; set; }

    /// <summary>
    /// Gets or sets an id for the kind of container, like <c>dispenser</c> or <c>ender_chest</c>.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the position of the block the container belongs to, if any.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Gets or sets the UUID of the player whose inventory this is; <see cref="Guid.Empty"/> otherwise.
    /// </summary>
    public Guid Owner { get; set; }

    /// <summary>
    /// Gets whether this is a player's own inventory, whose slots follow vanilla's player inventory numbering.
    /// </summary>
    public bool IsPlayerInventory { get; internal init; }

    // Its slots are its own, behind its own lock (see LocksOnlyItself).
    internal override bool LocksOnlyItself => true;

    /// <summary>
    /// Creates a container of 27 empty slots, a single chest's.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="type"/> isn't <see cref="InventoryType.Generic"/> or
    /// <see cref="InventoryType.ShulkerBox"/>.</exception>
    public Container(InventoryType type = InventoryType.Generic) : this(9 * 3, type) { }

    /// <summary>
    /// Creates a container of <paramref name="size"/> empty slots.
    /// </summary>
    /// <param name="size">A multiple of 9 up to 54, or 5 or 46.</param>
    /// <param name="type">How the container opens: <see cref="InventoryType.Generic"/> or
    /// <see cref="InventoryType.ShulkerBox"/>.</param>
    /// <exception cref="InvalidOperationException">The type or size isn't one of those.</exception>
    public Container(int size, InventoryType type = InventoryType.Generic) : base(size, type)
    {
        if (type is not (InventoryType.Generic or InventoryType.ShulkerBox))
            throw new InvalidOperationException("Inventory type can only be Generic or ShulkerBox");
        if (size % 9 is not 0 && size is not (46 or 5))
            throw new InvalidOperationException("Size must be divisble by 9");
        if (size > 9 * 6)
            throw new InvalidOperationException($"Size must be <= {9 * 6}");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A player's inventory fills its hotbar (slots 36 to 44) first, then its main inventory (9 to 35), and tops up a
    /// matching stack in place.
    /// </remarks>
    public override int AddItem(ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (this.IsPlayerInventory)
        {
            int? slot = InventoryItem(item, PlayerHotbarStart, PlayerHotbarEnd);
            if (slot is null)
                slot = InventoryItem(item, PlayerMainInventoryStart, PlayerMainInventoryEnd);
            if (slot is not null)
                return slot.Value;
        }
        else
        {
            return base.AddItem(item);
        }

        return -1;
    }

    private int? InventoryItem(ItemStack item, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            var invItem = this.items[i];

            if (invItem is not null && invItem.Holder == item.Holder)
            {
                if (invItem.Count >= 64)
                    continue;

                invItem.Count += item.Count;
                this.MarkChanged();

                return i;
            }

            if (invItem == null)
            {
                this[i] = item;

                return i;
            }
        }
        return null;
    }
}
