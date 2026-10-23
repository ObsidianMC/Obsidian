using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A stonecutter's window, like vanilla's <c>StonecutterMenu</c>: the input and the result of the chosen recipe.
/// </summary>
public sealed class Stonecutter : ResultContainer
{
    public const int InputSlot = 0;
    public const int ResultSlot = 1;

    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The index of the chosen recipe among those the input can be cut by; -1 for none.
    /// </summary>
    public int SelectedRecipe { get; set; } = -1;

    // The input item the choice was made for: another clears it.
    internal Item? LastInput { get; set; }

    public Stonecutter() : base(2, InventoryType.Stonecutter)
    {
        this.Title = "Stonecutter";
    }

    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
