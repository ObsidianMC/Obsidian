using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// An anvil's window, like vanilla's <c>AnvilMenu</c>: the item, what's combined with it, and the result, which costs
/// <see cref="Cost"/> levels.
/// </summary>
public sealed class AnvilContainer : ResultContainer
{
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 2;

    /// <summary>
    /// Gets the container's id, <c>anvil</c> unless another was given.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets or sets the position of the anvil block the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The name the player typed for the result, if any.
    /// </summary>
    public string? ItemName { get; set; }

    /// <summary>
    /// The levels the result costs.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>
    /// How many of the second item a repair with materials takes; 0 takes the whole stack.
    /// </summary>
    public int RepairItemCountCost { get; set; }

    /// <summary>
    /// Creates an anvil window's slots, with id <paramref name="id"/>.
    /// </summary>
    public AnvilContainer(string id = "anvil") : base(3, InventoryType.Anvil)
    {
        this.Id = id;
        this.Title = "Repair & Name";
    }

    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
