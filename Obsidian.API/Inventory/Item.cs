using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;

public readonly struct Item
{
    public required string UnlocalizedName { get; init; }

    public required Material Type { get; init; }

    public required short Id { get; init; }

    [SetsRequiredMembers]
    public Item(int id, string unlocalizedName, Material type)
    {
        Id = (short)id;
        UnlocalizedName = unlocalizedName;
        Type = type;
    }

    [SetsRequiredMembers]
    public Item(Item item)
    {
        Id = item.Id;
        UnlocalizedName = item.UnlocalizedName;
        Type = item.Type;
    }
}
