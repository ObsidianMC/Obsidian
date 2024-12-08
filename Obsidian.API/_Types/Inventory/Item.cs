using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

public readonly struct Item
{
    public required string UnlocalizedName { get; init; }

    public required Material Type { get; init; }

    public required short Id { get; init; }

    [SetsRequiredMembers]
    public Item(int id, string unlocalizedName, Material type)
    {
        this.Id = (short)id;
        this.UnlocalizedName = unlocalizedName;
        this.Type = type;
    }

    [SetsRequiredMembers]
    public Item(Item item)
    {
        this.Id = item.Id;
        this.UnlocalizedName = item.UnlocalizedName;
        this.Type = item.Type;
    }
}
