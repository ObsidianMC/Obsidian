namespace Obsidian.API;

public struct Item
{
    public string UnlocalizedName { get; }

    public Material Type { get; }

    public short Id { get; internal set; }

    public Item(int id, string unlocalizedName, Material type)
    {
        Id = (short)id;
        UnlocalizedName = unlocalizedName;
        Type = type;
    }

    public Item(Item item)
    {
        Id = item.Id;
        UnlocalizedName = item.UnlocalizedName;
        Type = item.Type;
    }
}
