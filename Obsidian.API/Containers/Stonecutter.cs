using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A stonecutter's window, like vanilla's <c>StonecutterMenu</c>: the input and the result of the chosen recipe.
/// </summary>
public sealed class Stonecutter : ResultContainer
{
    /// <summary>
    /// The input slot's index.
    /// </summary>
    public const int InputSlot = 0;
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 1;

    /// <summary>
    /// Gets or sets the position of the stonecutter the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The index of the chosen recipe among those the input can be cut by; -1 for none.
    /// </summary>
    public int SelectedRecipe { get; set; } = -1;

    // The input item the choice was made for: another clears it.
    internal Item? LastInput { get; set; }

    /// <summary>
    /// Creates a stonecutter window's slots.
    /// </summary>
    public Stonecutter() : base(2, InventoryType.Stonecutter)
    {
        this.Title = "Stonecutter";
    }

    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
