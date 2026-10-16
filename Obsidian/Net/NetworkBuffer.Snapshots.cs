using Obsidian.API.Inventory;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>
    /// Copies a stack's count and component patch for a click or an outgoing window snapshot. Component values are
    /// immutable and can be shared; legacy mutable payloads such as nested stacks are copied by their components.
    /// Saved unmodeled components are also retained, including those of nested stacks.
    /// </summary>
    internal static ItemStack SnapshotItemStack(ItemStack item) => new(item, item.Count);
}
