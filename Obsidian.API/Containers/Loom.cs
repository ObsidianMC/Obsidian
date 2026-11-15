using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A loom's window, like vanilla's <c>LoomMenu</c>: the banner, the dye, the pattern item and the result.
/// </summary>
public sealed class Loom : ResultContainer
{
    /// <summary>
    /// The banner slot's index.
    /// </summary>
    public const int BannerSlot = 0;
    /// <summary>
    /// The dye slot's index.
    /// </summary>
    public const int DyeSlot = 1;
    /// <summary>
    /// The banner pattern item slot's index.
    /// </summary>
    public const int PatternSlot = 2;
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 3;

    /// <summary>
    /// Gets or sets the position of the loom the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The index of the chosen pattern among those the pattern item (or none) offers; -1 for none.
    /// </summary>
    public int SelectedPattern { get; set; } = -1;

    // The patterns the loom offers for its pattern item, which the choice indexes.
    internal System.Collections.Immutable.ImmutableArray<int> SelectablePatterns { get; set; } = [];

    /// <summary>
    /// Creates a loom window's slots.
    /// </summary>
    public Loom() : base(4, InventoryType.Loom)
    {
        this.Title = "Loom";
    }

    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
