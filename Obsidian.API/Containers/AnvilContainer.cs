using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// An anvil's window, like vanilla's <c>AnvilMenu</c>: the item, what's combined with it, and the result, which costs
/// <see cref="Cost"/> levels.
/// </summary>
public sealed class AnvilContainer : ResultContainer
{
    public const int ResultSlot = 2;

    public string Id { get; }

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

    public AnvilContainer(string id = "anvil") : base(3, InventoryType.Anvil)
    {
        this.Id = id;
        this.Title = "Repair & Name";
    }

    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
