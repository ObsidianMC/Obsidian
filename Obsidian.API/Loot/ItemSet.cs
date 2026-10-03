using Obsidian.API.Inventory;
using System.Collections.Frozen;

namespace Obsidian.API.Loot;

/// <summary>
/// A set of items, like a vanilla item <c>HolderSet</c> (a list of ids or an item tag) resolved to item ids.
/// </summary>
public sealed class ItemSet
{
    private readonly FrozenSet<int> ids;

    /// <param name="ids">Item registry ids (<see cref="Item.Id"/>).</param>
    public ItemSet(params int[] ids) => this.ids = ids.ToFrozenSet();

    public bool Contains(Item item) => this.ids.Contains(item.Id);
}
