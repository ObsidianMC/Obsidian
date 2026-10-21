using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A loom's window, like vanilla's <c>LoomMenu</c>: the banner, the dye, the pattern item and the result.
/// </summary>
public sealed class Loom : ResultContainer
{
    public const int BannerSlot = 0;
    public const int DyeSlot = 1;
    public const int PatternSlot = 2;
    public const int ResultSlot = 3;

    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The index of the chosen pattern among those the pattern item (or none) offers; -1 for none.
    /// </summary>
    public int SelectedPattern { get; set; } = -1;

    // The patterns the loom offers for its pattern item, which the choice indexes.
    internal System.Collections.Immutable.ImmutableArray<int> SelectablePatterns { get; set; } = [];

    public Loom() : base(4, InventoryType.Loom)
    {
        this.Title = "Loom";
    }

    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
