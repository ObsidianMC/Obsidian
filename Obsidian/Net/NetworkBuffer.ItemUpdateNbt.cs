using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>Writes only the component patch clients may see, keeping disk saves on the full ToNbt path.</summary>
    internal static NbtCompound ItemUpdateNbt(ItemStack stack)
    {
        var tag = stack.ToNbt();
        ProjectUpdateItems(tag);
        return tag;
    }

    /// <summary>
    /// Filters item stacks inside an owned update snapshot, including vanilla's Items lists, single-item fields,
    /// and nested display or container items. Extra fields such as Slot are retained. Never pass disk-save data here.
    /// </summary>
    internal static void ProjectUpdateItems(INbtTag tag)
    {
        if (tag is NbtCompound compound)
        {
            if (compound.TryGetTag<NbtTag<string>>("id", out _)
                && (compound.HasTag("components") || compound.HasTag("count") || compound.HasTag("Count")))
            {
                var stack = compound.ItemFromNbt();
                compound.Remove("components");
                if (stack is not null)
                {
                    var visible = new ItemStack(stack.Holder, stack.Count, SentPatch(stack));
                    foreach (var type in stack.RemoveComponents)
                        visible.Remove(type);

                    if (visible.ToNbt().TryGetTag<NbtCompound>("components", out var components))
                        compound.Add(components);
                }
            }

            foreach (var (_, child) in compound)
                ProjectUpdateItems(child);
        }
        else if (tag is NbtList list)
        {
            foreach (var child in list)
                ProjectUpdateItems(child);
        }
    }
}
