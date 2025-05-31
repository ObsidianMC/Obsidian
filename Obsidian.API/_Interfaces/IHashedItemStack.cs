using Obsidian.API.Inventory;

namespace Obsidian.API;
public interface IHashedItemStack
{
    public Dictionary<DataComponentType, int> HashedComponents { get; }

    public List<DataComponentType> ComponentsToRemove { get; }

    public int Count { get; set; }

    public Item Holder { get; }

    public Material Type { get; }

    public bool Compare(ItemStack other);
}
