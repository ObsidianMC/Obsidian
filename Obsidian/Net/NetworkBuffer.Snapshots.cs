using Obsidian.API.Inventory;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>
    /// Copies a stack through the network codec, so that the copy shares no component, or component value, with it:
    /// later changes to either don't reach the other. The saved components Obsidian doesn't model, which the codec
    /// leaves out, are kept; they're never changed, so the copy shares them.
    /// </summary>
    /// <remarks>
    /// A click's <c>Item</c> is such a copy, and so are the stacks of the packets a click captures inside a container's
    /// lock, which are written after it.
    /// </remarks>
    internal static ItemStack SnapshotItemStack(ItemStack item)
    {
        // The codec writes an empty stack as nothing; a plain copy of it has no components worth separating.
        if (item.IsAir || item.Count <= 0)
            return new ItemStack(item, item.Count);

        var writer = new NetworkBuffer();
        writer.WriteItemStack(item);

        var reader = new NetworkBuffer(writer.data.AsSpan(0, writer.Offset).ToArray());
        var copy = reader.ReadRequiredItemStack();

        return new ItemStack(copy, copy.Count) { UnmodeledComponents = item.UnmodeledComponents };
    }
}
