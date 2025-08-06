namespace Obsidian.API.Inventory;

public readonly record struct Enchantment
{
    public required int Id { get; init; }

    public required int Level { get; init; }
}
