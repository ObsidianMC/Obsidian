using Obsidian.Net;

namespace Obsidian.Utilities;
public partial class Extensions
{
    public static void Write(this ItemStack itemStack, MinecraftStream stream)
    {
        var item = itemStack.AsItem();
        var meta = itemStack.ItemMeta;

        stream.WriteVarInt(itemStack.Count);

        //Stop serializing if item is invalid
        if (itemStack.Count <= 0)
            return;

        stream.WriteVarInt(item.Id);

        if (!meta.HasTags())
            return;
    }
}
