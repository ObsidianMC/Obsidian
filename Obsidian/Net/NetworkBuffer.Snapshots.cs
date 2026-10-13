using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>
    /// Copies a stack through the network codec, so that the copy shares no component, or component value, with it:
    /// later changes to either don't reach the other. The saved components Obsidian doesn't model, which the codec
    /// leaves out, are kept, the stack's and those of the stacks nested in its components; they're never changed, so
    /// the copy shares them.
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

        return KeepUnmodeled(reader.ReadRequiredItemStack(), item);
    }

    /// <summary>
    /// Gives <paramref name="copy"/>, decoded from <paramref name="source"/>, the unmodeled components the codec left
    /// out: the source's, and those of the stacks nested in its components (a bundle's contents, a container's, charged
    /// projectiles, a use remainder), matched by component and position, which the codec keeps.
    /// </summary>
    /// <remarks>
    /// Only the copy's own component values, which the codec just made, are changed.
    /// </remarks>
    private static ItemStack KeepUnmodeled(ItemStack copy, ItemStack source)
    {
        foreach (var component in copy)
        {
            switch (component)
            {
                case SimpleDataComponent<ItemStack> { Value: ItemStack nested } single
                    when source.GetComponent<SimpleDataComponent<ItemStack>>(component.Type)?.Value
                        is ItemStack original:
                {
                    var kept = KeepUnmodeled(nested, original);
                    if (!ReferenceEquals(kept, nested))
                        single.Value = kept;

                    break;
                }
                case SimpleDataComponent<ItemStack[]> { Value: ItemStack[] nested }
                    when source.GetComponent<SimpleDataComponent<ItemStack[]>>(component.Type)?.Value
                        is ItemStack[] originals:
                {
                    for (var i = 0; i < Math.Min(nested.Length, originals.Length); i++)
                    {
                        if (nested[i] is not ItemStack stack || originals[i] is not ItemStack original)
                            continue;

                        var kept = KeepUnmodeled(stack, original);
                        if (!ReferenceEquals(kept, stack))
                            nested[i] = kept;
                    }

                    break;
                }
            }
        }

        return source.UnmodeledComponents is null
            ? copy
            : new ItemStack(copy, copy.Count) { UnmodeledComponents = source.UnmodeledComponents };
    }
}
