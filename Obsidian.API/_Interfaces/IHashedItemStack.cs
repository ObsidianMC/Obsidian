using Obsidian.API.Inventory;

namespace Obsidian.API;
/// <summary>
/// An item stack as a client describes it in a container click: its item, count and a hash of each component, rather
/// than the components themselves.
/// </summary>
public interface IHashedItemStack
{
    /// <summary>
    /// Gets the CRC32 hash of each component the client's stack has, by component type.
    /// </summary>
    public Dictionary<DataComponentType, int> HashedComponents { get; }

    /// <summary>
    /// Gets the components the client's stack removes from the item's defaults.
    /// </summary>
    public List<DataComponentType> ComponentsToRemove { get; }

    /// <summary>
    /// Gets or sets the number of items in the stack.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets the stack's item.
    /// </summary>
    public Item Holder { get; }

    /// <summary>
    /// Returns whether <paramref name="other"/> is the same item and its components match
    /// <see cref="HashedComponents"/>. The count and <see cref="ComponentsToRemove"/> aren't compared.
    /// </summary>
    /// <param name="other">The server's stack to compare with.</param>
    public bool Compare(ItemStack other);
}
